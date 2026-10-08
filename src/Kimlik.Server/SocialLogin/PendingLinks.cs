using System.Security.Claims;
using Kimlik.Application.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Server.SocialLogin;

/// <summary>An account at a provider waiting to be linked to a Kimlik account.</summary>
public sealed record PendingLink(ExternalProvider Provider, string Key, string? Email)
{
    public ExternalLogin Login => new(Provider.Name, Key, Provider.DisplayName);
}

/// <summary>
/// Holds an account at a provider until the person proves they own the Kimlik account to link it to, by signing in
/// to it, and confirms. It waits in Identity's short-lived external sign-in cookie.
/// </summary>
public sealed class PendingLinks(IHttpContextAccessor accessor, ExternalProviders providers)
{
    private const string ProviderClaim = "kimlik:provider";
    private const string KeyClaim = "kimlik:provider_key";

    private HttpContext Context => accessor.HttpContext ?? throw new InvalidOperationException("Pending links need an HTTP request.");

    public Task HoldAsync(ExternalIdentity identity)
    {
        var claims = new ClaimsIdentity(IdentityConstants.ExternalScheme);
        claims.AddClaim(new Claim(ProviderClaim, identity.Provider.Name));
        claims.AddClaim(new Claim(KeyClaim, identity.Key));
        if (identity.Email is not null)
        {
            claims.AddClaim(new Claim(ClaimTypes.Email, identity.Email));
        }

        return Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(claims));
    }

    public async Task<PendingLink?> FindAsync()
    {
        var pending = await Context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        return pending.Principal is { } principal
            && providers.Find(principal.FindFirstValue(ProviderClaim)) is { } provider
            && principal.FindFirstValue(KeyClaim) is { } key
                ? new PendingLink(provider, key, principal.FindFirstValue(ClaimTypes.Email))
                : null;
    }

    public Task ClearAsync() => Context.SignOutAsync(IdentityConstants.ExternalScheme);
}
