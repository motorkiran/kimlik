using System.Net;
using System.Text.Json;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Oidc;

/// <summary>The device authorization grant (RFC 8628): a command-line tool signs a person in from their browser.</summary>
public sealed class DeviceFlowTests(KimlikServerFixture server)
{
    private const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";

    [Fact]
    public async Task Device_GetsTokens_OnceThePersonAllowsIt()
    {
        var clientId = await CreateCommandLineToolAsync();
        var user = await server.CreateUserAsync();
        using var device = server.CreateClient();
        var authorization = await AskForADeviceCodeAsync(device, clientId);

        using var early = await PollAsync(device, clientId, authorization);
        (await early.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("authorization_pending");

        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);
        var page = await browser.GetPageAsync(new Uri(authorization.GetProperty("verification_uri_complete").GetString()!).PathAndQuery);
        page.Text.ShouldContain("Allow this device?");
        page.Text.ShouldContain("Terminal");
        using var allowed = await browser.SubmitAsync(page, formSelector: "#decide", submitter: ("decision", "allow"));
        allowed.Headers.Location!.OriginalString.ShouldBe("/connect/verify?outcome=approved");

        using var granted = await PollAsync(device, clientId, authorization);
        var tokens = await granted.ReadJsonAsync();
        tokens.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
        tokens.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();
        (await server.QueryDatabaseAsync(context => context.AuditEvents.CountAsync(
            auditEvent => auditEvent.Action == AuditActions.UserDeviceApproved && auditEvent.SubjectId == user.Id.ToString(), TestContext.Current.CancellationToken)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task DeniedDevice_GetsNoTokens()
    {
        var clientId = await CreateCommandLineToolAsync();
        var user = await server.CreateUserAsync();
        using var device = server.CreateClient();
        var authorization = await AskForADeviceCodeAsync(device, clientId);
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var entry = await browser.GetPageAsync("/connect/verify");
        var page = await Browser.ReadPageAsync(await browser.GetAsync($"/connect/verify?user_code={Uri.EscapeDataString(authorization.GetProperty("user_code").GetString()!)}"));
        using var denied = await browser.SubmitAsync(page, formSelector: "#decide", submitter: ("decision", "deny"));

        entry.Document.QuerySelector("#enter-code").ShouldNotBeNull();
        denied.Headers.Location!.OriginalString.ShouldBe("/connect/verify?outcome=denied");
        using var refused = await PollAsync(device, clientId, authorization);
        (await refused.ReadJsonAsync()).GetProperty("error").GetString().ShouldBe("access_denied");
    }

    [Fact]
    public async Task WrongCode_IsTurnedAway()
    {
        var user = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var page = await browser.GetPageAsync("/connect/verify?user_code=WRONG-CODE");

        page.Text.ShouldContain("That code is not valid, or it has expired.");
    }

    private async Task<string> CreateCommandLineToolAsync()
    {
        using var api = await server.CreateApiClientAsync();
        var clientId = $"cli-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Terminal",
            Type = ClientType.Native,
            Scopes = ["openid", "offline_access"],
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return clientId;
    }

    private static async Task<JsonElement> AskForADeviceCodeAsync(HttpClient device, string clientId)
    {
        using var response = await device.PostFormAsync("/connect/device", [new("client_id", clientId), new("scope", "openid offline_access")]);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> PollAsync(HttpClient device, string clientId, JsonElement authorization) =>
        device.PostFormAsync("/connect/token",
        [
            new("grant_type", DeviceCodeGrant),
            new("client_id", clientId),
            new("device_code", authorization.GetProperty("device_code").GetString()!),
        ]);
}
