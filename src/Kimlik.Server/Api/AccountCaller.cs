using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Api;

internal static class AccountCaller
{
    /// <summary>The user an access token was issued to; <see langword="null"/> for a service client acting on its own behalf.</summary>
    public static Guid? UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(Claims.Subject) is { } subject
        && subject != principal.FindFirstValue(Claims.ClientId)
        && Guid.TryParse(subject, out var userId)
            ? userId
            : null;
}
