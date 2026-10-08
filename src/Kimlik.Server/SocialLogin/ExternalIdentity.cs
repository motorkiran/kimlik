using System.Security.Claims;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.SocialLogin;

/// <summary>Who a provider says the person signing in is.</summary>
public sealed record ExternalIdentity(
    ExternalProvider Provider, string Key, string? Email, bool EmailVerified, string? GivenName, string? FamilyName, string? Locale)
{
    public ExternalLogin Login => new(Provider.Name, Key, Provider.DisplayName);

    /// <summary>
    /// Reads a sign-in at a provider. OpenIddict maps each provider's own claims to the <see cref="ClaimTypes"/> ones;
    /// the OpenID Connect names are the fallback. An address only counts as verified when the provider says so and
    /// Kimlik trusts it to.
    /// </summary>
    public static ExternalIdentity? From(ClaimsPrincipal principal, ExternalProviders providers)
    {
        if (providers.Find(principal.GetClaim(Claims.Private.ProviderName)) is not { } provider
            || (principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.GetClaim(Claims.Subject)) is not { Length: > 0 } key)
        {
            return null;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.GetClaim(Claims.Email);
        var verified = provider.TrustEmail && email is not null && bool.TryParse(principal.GetClaim(Claims.EmailVerified), out var isVerified) && isVerified;

        var givenName = principal.FindFirstValue(ClaimTypes.GivenName) ?? principal.GetClaim(Claims.GivenName);
        var familyName = principal.FindFirstValue(ClaimTypes.Surname) ?? principal.GetClaim(Claims.FamilyName);

        // Some providers, such as GitHub, only have a full name, which cannot be split reliably; it goes in whole.
        if (givenName is null && familyName is null)
        {
            givenName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.GetClaim(Claims.Name);
        }

        return new ExternalIdentity(
            provider, key, email, verified, Limit(givenName, User.NameMaxLength), Limit(familyName, User.NameMaxLength), Limit(principal.GetClaim(Claims.Locale), 16));
    }

    private static string? Limit(string? value, int length) => value is { Length: > 0 } ? value[..Math.Min(value.Length, length)] : null;
}
