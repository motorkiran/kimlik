using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.Oidc;

internal sealed record TestClient(string ClientId, string ClientSecret);

/// <summary>Registers clients and API scopes with unique names, so tests sharing the database stay independent.</summary>
internal static class TestClients
{
    public const string ApiScope = "orders";
    public const string ApiResource = "orders-api";

    public static Task<TestClient> CreateServiceClientAsync(this KimlikServerFixture server) =>
        server.WithServicesAsync(async services =>
        {
            await EnsureApiScopeAsync(services.GetRequiredService<IOpenIddictScopeManager>());

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

    private static async Task EnsureApiScopeAsync(IOpenIddictScopeManager scopes)
    {
        if (await scopes.FindByNameAsync(ApiScope) is not null)
        {
            return;
        }

        try
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = ApiScope, Resources = { ApiResource } });
        }
        catch (OpenIddictExceptions.ValidationException)
        {
            // Another test created it concurrently.
        }
    }
}
