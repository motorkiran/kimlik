using System.Net;
using Kimlik.Client;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Oidc;
using Microsoft.Extensions.DependencyInjection;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Tests.Sdk;

/// <summary>The Management API through Kimlik.Client, as an application backend would use it.</summary>
public sealed class KimlikClientTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_CanBeManaged_ThroughTheClient()
    {
        await using var services = await CreateServicesAsync();
        var kimlik = services.GetRequiredService<KimlikClient>();
        var email = $"sdk-{Guid.NewGuid():N}@example.com";

        var created = await kimlik.Users.CreateAsync(new CreateUserRequest { Email = email, GivenName = "Ada", FamilyName = "Lovelace" }, CancellationToken);

        // Only what the update sets is sent, so the family name stays.
        var updated = await kimlik.Users.UpdateAsync(created.Id, new UpdateUserRequest { GivenName = "Augusta" }, CancellationToken);
        updated.Name.ShouldBe("Augusta Lovelace");

        (await kimlik.Users.ListAsync(search: email, cancellationToken: CancellationToken)).Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        await kimlik.Users.SuspendAsync(created.Id, CancellationToken);
        (await kimlik.Users.ListAsync(search: email, status: UserStatus.Suspended, cancellationToken: CancellationToken)).Items.ShouldHaveSingleItem();

        await kimlik.Users.DeleteAsync(created.Id, CancellationToken);
        var gone = await Should.ThrowAsync<KimlikApiException>(() => kimlik.Users.GetAsync(created.Id, CancellationToken));
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        gone.Code.ShouldBe("user.not_found");
    }

    [Fact]
    public async Task InvalidRequest_ReportsTheFields()
    {
        await using var services = await CreateServicesAsync();
        var kimlik = services.GetRequiredService<KimlikClient>();

        var invalid = await Should.ThrowAsync<KimlikApiException>(() =>
            kimlik.Roles.CreateAsync(new CreateRoleRequest { Key = "billing", Name = new string('x', 101) }, CancellationToken));

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Code.ShouldBe("request.invalid");
        invalid.Errors.Keys.ShouldContain(field => field.Equals("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccessToken_IsReused_AcrossCalls()
    {
        var tokenRequests = new CountingHandler();
        await using var services = await CreateServicesAsync(tokenRequests);
        var kimlik = services.GetRequiredService<KimlikClient>();

        await kimlik.Permissions.ListAsync(cancellationToken: CancellationToken);
        await kimlik.Roles.ListAsync(scope: RoleScope.Global, cancellationToken: CancellationToken);

        // The discovery document, then a single token.
        tokenRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WrongSecret_IsReported()
    {
        await using var services = await CreateServicesAsync(secret: "not the secret of this client");
        var kimlik = services.GetRequiredService<KimlikClient>();

        var refused = await Should.ThrowAsync<KimlikApiException>(() => kimlik.Users.ListAsync(cancellationToken: CancellationToken));

        refused.Code.ShouldBe("invalid_client");
    }

    /// <summary>A service client holding <see cref="SystemRoles.Admin"/>, wired to the in-memory Kimlik server.</summary>
    private async Task<ServiceProvider> CreateServicesAsync(CountingHandler? tokenRequests = null, string? secret = null)
    {
        var serviceClient = await server.CreateServiceClientAsync(KimlikScopes.Api);
        await server.AssignToClientAsync(serviceClient.ClientId, SystemRoles.Admin);

        var services = new ServiceCollection();
        services.AddKimlikClient(options =>
        {
            options.Authority = new Uri(TestConfiguration.PublicUrl);
            options.ClientId = serviceClient.ClientId;
            options.ClientSecret = secret ?? serviceClient.ClientSecret;
        });
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => server.Server.CreateHandler()));

        if (tokenRequests is not null)
        {
            services.AddHttpClient(KimlikClientDefaults.TokenHttpClientName).AddHttpMessageHandler(() => tokenRequests.Fresh());
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Counts the requests of every handler instance it hands out.</summary>
    private sealed class CountingHandler
    {
        private int _count;

        public int Count => _count;

        public DelegatingHandler Fresh() => new Counter(this);

        private sealed class Counter(CountingHandler owner) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner._count);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
