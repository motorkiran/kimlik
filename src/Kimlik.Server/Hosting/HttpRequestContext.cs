using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Api;
using Kimlik.Server.Identity;
using Kimlik.Server.Oidc;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Hosting;

/// <summary>Describes the caller from the current HTTP request; outside of a request Kimlik itself is the actor.</summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public AuditActor Actor => accessor.HttpContext?.User switch
    {
        null => AuditActor.System,
        { Identity.IsAuthenticated: true } principal => ActorOf(principal),
        _ => AuditActor.Anonymous,
    };

    public IReadOnlySet<string> Permissions => accessor.HttpContext?.User is { } principal
        ? AccessClaims.GetPermissions(principal)
        : new HashSet<string>();

    public IPAddress? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress;

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null;

    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;

    private static AuditActor ActorOf(ClaimsPrincipal principal)
    {
        // Access tokens name their subject in "sub"; the sign-in session uses Identity's claim type.
        var subject = principal.FindFirstValue(Claims.Subject) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        // An administrator acting as a user, in a session or with tokens that impersonate them, is the one who acts.
        if (Guid.TryParse(SignInFlow.ActorOf(principal) ?? OidcPrincipalFactory.GetActor(principal), out var administratorId))
        {
            return AuditActor.User(administratorId);
        }

        // A service client acting on its own behalf is the subject of its tokens (see TokenEndpoint).
        if (subject is not null && subject == principal.FindFirstValue(Claims.ClientId))
        {
            return AuditActor.Client(subject);
        }

        return Guid.TryParse(subject, out var userId) ? AuditActor.User(userId) : AuditActor.Anonymous;
    }
}
