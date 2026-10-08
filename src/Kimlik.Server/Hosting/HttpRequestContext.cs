using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;

namespace Kimlik.Server.Hosting;

/// <summary>Describes the caller from the current HTTP request; outside of a request Kimlik itself is the actor.</summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public AuditActor Actor => accessor.HttpContext switch
    {
        null => AuditActor.System,
        { User.Identity.IsAuthenticated: true } context when Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            => AuditActor.User(userId),
        _ => AuditActor.Anonymous,
    };

    public IPAddress? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress;

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null;

    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;
}
