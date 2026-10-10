using System.Security.Claims;
using System.Text.Json;
using Kimlik.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.AspNetCore;

/// <summary>
/// The caller behind a Kimlik access token or API key: a user, or a service client or an organization's API key acting
/// on its own behalf. Minimal API endpoints can take it as a parameter; elsewhere, use <see cref="FromPrincipal"/>.
/// </summary>
public sealed class KimlikUser
{
    /// <summary>The client the token was issued to (RFC 9068, section 2.2).</summary>
    private const string ClientIdClaim = "client_id";

    /// <summary>Who acts for the subject (RFC 8693, section 4.1).</summary>
    private const string ActorClaim = "act";

    private KimlikUser(ClaimsPrincipal principal, string subject)
    {
        Subject = subject;
        ClientId = principal.FindFirstValue(ClientIdClaim);
        Name = principal.FindFirstValue(JwtRegisteredClaimNames.Name);
        Email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        PhoneNumber = principal.FindFirstValue(JwtRegisteredClaimNames.PhoneNumber);
        Roles = Values(principal, KimlikClaimTypes.Roles);
        Permissions = Values(principal, KimlikClaimTypes.Permissions);
        OrganizationId = Guid.TryParse(principal.FindFirstValue(KimlikClaimTypes.OrganizationId), out var organizationId) ? organizationId : null;
        OrganizationRoles = Values(principal, KimlikClaimTypes.OrganizationRoles);
        Plan = principal.FindFirstValue(KimlikClaimTypes.Plan);
        ApiKeyId = Guid.TryParse(principal.FindFirstValue(KimlikClaimTypes.ApiKeyId), out var apiKeyId) ? apiKeyId : null;
        ActorId = ActorOf(principal);
    }

    /// <summary>The user ID, the client ID of a service client, or the ID of an organization's API key.</summary>
    public string Subject { get; }

    /// <summary>The client the token was issued to.</summary>
    public string? ClientId { get; }

    /// <summary>Whether the caller is a service client acting on its own behalf rather than for a user.</summary>
    public bool IsServiceClient => Subject == ClientId;

    /// <summary>The API key the caller authenticated with, when it used one rather than an access token.</summary>
    public Guid? ApiKeyId { get; }

    /// <summary>The user's ID; <see langword="null"/> for a service client or an organization's API key.</summary>
    public Guid? UserId => !IsServiceClient && Subject != ApiKeyId?.ToString() && Guid.TryParse(Subject, out var id) ? id : null;

    /// <summary>Present when the client was granted the <c>profile</c> scope.</summary>
    public string? Name { get; }

    /// <summary>Present when the client was granted the <c>email</c> scope.</summary>
    public string? Email { get; }

    /// <summary>The verified phone number, in E.164; present when the client was granted the <c>phone</c> scope.</summary>
    public string? PhoneNumber { get; }

    /// <summary>The user's global roles, or a service client's roles.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>What the caller may do: the permissions of its global roles and of its roles in <see cref="OrganizationId"/>.</summary>
    public IReadOnlySet<string> Permissions { get; }

    /// <summary>The organization the token acts in, if the app signed the user in to one, or whose API key it is.</summary>
    public Guid? OrganizationId { get; }

    /// <summary>The user's roles in <see cref="OrganizationId"/>.</summary>
    public IReadOnlySet<string> OrganizationRoles { get; }

    /// <summary>The key of the plan in effect: the organization's in an organization context, otherwise the user's.</summary>
    public string? Plan { get; }

    /// <summary>
    /// The administrator acting as the user, for support, when the token was issued while they signed in as them. Apps
    /// can show it, record it, or refuse what only the user may do.
    /// </summary>
    public Guid? ActorId { get; }

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <summary>The caller of an authenticated request, or <see langword="null"/> when it has no Kimlik token.</summary>
    public static KimlikUser? FromPrincipal(ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } subject ? new KimlikUser(principal, subject) : null;

    /// <summary>Binds the caller as a minimal API parameter.</summary>
    public static ValueTask<KimlikUser?> BindAsync(HttpContext context) => ValueTask.FromResult(FromPrincipal(context.User));

    private static Guid? ActorOf(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue(ActorClaim) is not { Length: > 0 } actor)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(actor);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty(JwtRegisteredClaimNames.Sub, out var subject) && subject.ValueKind == JsonValueKind.String
                && Guid.TryParse(subject.GetString(), out var actorId)
                ? actorId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HashSet<string> Values(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
}
