using System.Collections.Concurrent;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Organizations;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.SocialLogin;

/// <summary>Sign-in through organizations' own identity providers, their SSO connections.</summary>
internal static class SingleSignOn
{
    /// <summary>Where every connection's provider sends people back, relative to the installation's URL.</summary>
    public const string CallbackPath = "signin/sso/callback";

    /// <summary>Sends the browser to the connection's provider, with the address as a hint, and back to <paramref name="returnUrl"/>.</summary>
    public static ChallengeResult Challenge(SsoProvider connection, string? email, string? returnUrl)
    {
        var properties = new AuthenticationProperties { RedirectUri = AccountLinks.IsLocalUrl(returnUrl) ? returnUrl : "/" };
        properties.Items[OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = connection.LoginProvider;
        if (!string.IsNullOrWhiteSpace(email))
        {
            properties.Items[OpenIddictClientAspNetCoreConstants.Properties.LoginHint] = email.Trim();
        }

        return new ChallengeResult(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme, properties);
    }
}

/// <summary>
/// OpenIddict's client service, which also finds the client registrations of SSO connections. Connections live in the
/// database, so they change without a restart, on every instance: each lookup reads the connection, and a registration
/// is built again only when the connection changed, keeping the provider's discovered configuration and keys meanwhile.
/// </summary>
internal sealed class SsoClientService(IServiceProvider provider, IServiceScopeFactory scopes) : OpenIddictClientService(provider)
{
    private const string UpdatedAtProperty = "kimlik:updated_at";

    private readonly ConcurrentDictionary<Guid, OpenIddictClientRegistration> _registrations = new();

    public override async ValueTask<OpenIddictClientRegistration> GetClientRegistrationByIdAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (SsoConnection.IdOf(identifier) is not { } id)
        {
            return await base.GetClientRegistrationByIdAsync(identifier, cancellationToken);
        }

        await using var scope = scopes.CreateAsyncScope();
        var connection = await scope.ServiceProvider.GetRequiredService<SsoDirectory>().FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The SSO connection was deleted during the sign-in.");

        if (_registrations.TryGetValue(id, out var cached) && cached.Properties[UpdatedAtProperty] is DateTimeOffset updatedAt && updatedAt == connection.UpdatedAt)
        {
            return cached;
        }

        var registration = Build(connection);
        _registrations[id] = registration;
        return registration;
    }

    /// <summary>The registration of a connection, as OpenIddict would complete one from its options.</summary>
    private OpenIddictClientRegistration Build(SsoProvider connection)
    {
        var issuer = new Uri(connection.Issuer, UriKind.Absolute);
        var registration = new OpenIddictClientRegistration
        {
            RegistrationId = connection.LoginProvider,
            ProviderDisplayName = connection.Name,
            Issuer = issuer,
            ClientId = connection.ClientId,
            ClientSecret = connection.ClientSecret,
            ClientType = ClientTypes.Confidential,
            RedirectUri = new Uri(SingleSignOn.CallbackPath, UriKind.Relative),

            // OpenID Connect Discovery appends its path to the issuer's, whether or not that ends with a slash.
            ConfigurationEndpoint = new Uri($"{issuer.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration", UriKind.Absolute),
        };

        registration.Scopes.UnionWith([Scopes.OpenId, Scopes.Email, Scopes.Profile]);
        registration.Properties[UpdatedAtProperty] = connection.UpdatedAt;
        registration.ConfigurationManager = new ConfigurationManager<OpenIddictConfiguration>(
            registration.ConfigurationEndpoint.AbsoluteUri, new OpenIddictClientRetriever(this, registration))
        {
            AutomaticRefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultAutomaticRefreshInterval,
            RefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultRefreshInterval,
        };

        return registration;
    }
}
