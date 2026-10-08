using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Oidc;

internal sealed record TestClient(string ClientId, string ClientSecret);

internal sealed record TestWebClient(string ClientId)
{
    public const string RedirectUri = "https://app.example.com/callback";
    public const string PostLogoutRedirectUri = "https://app.example.com/signed-out";
}

/// <summary>Registers clients and API scopes with unique names, so tests sharing the database stay independent.</summary>
internal static class TestClients
{
    public const string ApiScope = "orders";
    public const string ApiResource = "orders-api";

    public static Task<TestClient> CreateServiceClientAsync(this KimlikServerFixture server) =>
        server.WithServicesAsync(async services =>
        {
            var client = new TestClient($"service-{Guid.NewGuid():N}", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
            await services.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientId,
                ClientSecret = client.ClientSecret,
                ClientType = ClientTypes.Confidential,
                DisplayName = "Billing worker",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Introspection,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + ApiScope,
                },
            });

            return client;
        });

    /// <summary>A public browser-based client (SPA) using authorization code with PKCE.</summary>
    public static Task<TestWebClient> CreateWebClientAsync(this KimlikServerFixture server, string consentType = ConsentTypes.Implicit) =>
        server.WithServicesAsync(async services =>
        {
            var client = new TestWebClient($"web-{Guid.NewGuid():N}");
            await services.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientId,
                ClientType = ClientTypes.Public,
                ConsentType = consentType,
                DisplayName = "Orders web app",
                RedirectUris = { new Uri(TestWebClient.RedirectUri) },
                PostLogoutRedirectUris = { new Uri(TestWebClient.PostLogoutRedirectUri) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                    Permissions.Prefixes.Scope + ApiScope,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            });

            return client;
        });

    /// <summary>Registers the API scope every test client may request; runs once when the test server starts.</summary>
    public static Task CreateApiScopeAsync(IServiceProvider services) =>
        services.GetRequiredService<IOpenIddictScopeManager>().CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = ApiScope,
            DisplayName = "Manage your orders",
            Resources = { ApiResource },
        }).AsTask();
}
