using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Passkeys;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>A new client secret can leave the previous one working for a while, so that the client switches without downtime.</summary>
public sealed class ClientSecretRotationTests(KimlikServerFixture server)
{
    [Fact]
    public async Task PreviousSecret_KeepsWorking_UntilItExpires()
    {
        var clock = new ShiftedTimeProvider();
        await using var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        var client = await server.CreateServiceClientAsync();
        using var api = await server.CreateApiClientAsync();

        using var rotated = await api.Http.PostJsonAsync(
            $"/api/v1/clients/{await IdOfAsync(client)}/secret", new RegenerateClientSecretRequest { KeepPreviousSecretForDays = 1 });
        var secret = await rotated.ReadAsync<ClientSecretResponse>();
        secret.PreviousSecretExpiresAt.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(23));

        using var http = kimlik.CreateClient();
        (await RequestTokenAsync(http, client)).ShouldBe(HttpStatusCode.OK);
        (await RequestTokenAsync(http, client with { ClientSecret = secret.ClientSecret })).ShouldBe(HttpStatusCode.OK);

        clock.Offset = TimeSpan.FromDays(2);
        (await RequestTokenAsync(http, client)).ShouldBe(HttpStatusCode.Unauthorized);
        (await RequestTokenAsync(http, client with { ClientSecret = secret.ClientSecret })).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NewSecret_WithoutOverlap_EndsThePreviousOneAtOnce()
    {
        var client = await server.CreateServiceClientAsync();
        using var api = await server.CreateApiClientAsync();
        var id = await IdOfAsync(client);

        using var rotated = await api.Http.PostAsync($"/api/v1/clients/{id}/secret");
        var secret = await rotated.ReadAsync<ClientSecretResponse>();
        using var http = server.CreateClient();

        secret.PreviousSecretExpiresAt.ShouldBeNull();
        (await RequestTokenAsync(http, client)).ShouldBe(HttpStatusCode.Unauthorized);
        (await RequestTokenAsync(http, client with { ClientSecret = secret.ClientSecret })).ShouldBe(HttpStatusCode.OK);

        using var tooLong = await api.Http.PostJsonAsync($"/api/v1/clients/{id}/secret", new RegenerateClientSecretRequest { KeepPreviousSecretForDays = 31 });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private Task<Guid> IdOfAsync(TestClient client) =>
        server.QueryDatabaseAsync(context => context.Applications.Where(application => application.ClientId == client.ClientId).Select(application => application.Id).SingleAsync());

    private static async Task<HttpStatusCode> RequestTokenAsync(HttpClient http, TestClient client)
    {
        using var response = await http.PostFormAsync("/connect/token", [new("grant_type", "client_credentials"), new("scope", TestClients.ApiScope)], client);
        return response.StatusCode;
    }
}
