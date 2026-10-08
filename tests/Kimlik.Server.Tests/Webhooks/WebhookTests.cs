using System.Net;
using Kimlik.AspNetCore;
using Kimlik.Contracts.Management;
using Kimlik.Contracts.Webhooks;
using Kimlik.Server.Tests.Api;

namespace Kimlik.Server.Tests.Webhooks;

/// <summary>Webhook endpoints receiving signed events, through failures and retries.</summary>
public sealed class WebhookTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Endpoint_ReceivesSignedEvents_ThatNameWhatChanged()
    {
        using var admin = await server.CreateApiClientAsync();
        var url = server.Webhooks.NewEndpoint();
        var endpoint = await CreateEndpointAsync(admin, url, WebhookEventTypes.UserCreated);

        using var created = await admin.Http.PostJsonAsync("/api/v1/users", new CreateUserRequest { Email = $"hooked-{Guid.NewGuid():N}@example.com" });
        var user = await created.ReadAsync<UserResponse>();

        var received = (await server.Webhooks.WaitForAsync(url, webhook => webhook.Body.Contains(user.Id.ToString(), StringComparison.Ordinal))).Single();
        KimlikWebhook.TryVerify(received.Headers, received.Body, endpoint.Secret, out var webhookEvent).ShouldBeTrue();
        webhookEvent.Type.ShouldBe(WebhookEventTypes.UserCreated);
        webhookEvent.Data.SubjectType.ShouldBe("user");
        webhookEvent.Data.SubjectId.ShouldBe(user.Id.ToString());
        webhookEvent.Data.Actor.ShouldBe(new WebhookEventActor("client", admin.ClientId));

        // A forged or altered request fails the check.
        KimlikWebhook.TryVerify(received.Headers, received.Body.Replace("user.created", "user.deleted", StringComparison.Ordinal), endpoint.Secret, out _).ShouldBeFalse();
        KimlikWebhook.TryVerify(received.Headers, received.Body, "whsec_" + Convert.ToBase64String(new byte[32]), out _).ShouldBeFalse();

        using var listed = await admin.Http.GetAsync($"/api/v1/webhooks/deliveries?endpointId={endpoint.Endpoint.Id}&limit=200", CancellationToken);
        var delivery = (await listed.ReadAsync<Page<WebhookDeliveryResponse>>()).Items.Single(candidate => candidate.EventId.ToString() == received.Id);
        delivery = await WaitForDeliveryAsync(admin, delivery.Id, WebhookDeliveryStatus.Succeeded);
        delivery.ResponseStatusCode.ShouldBe(200);
        await DeleteEndpointAsync(admin, endpoint.Endpoint.Id);
    }

    [Fact]
    public async Task FailedDeliveries_AreRetried_WithTheSameId()
    {
        using var admin = await server.CreateApiClientAsync();
        var url = server.Webhooks.NewEndpoint(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable);

        // Disabled, the endpoint gets only the test events it is sent, and none of what other tests do.
        var endpoint = await CreateEndpointAsync(admin, url, enabled: false);
        var test = await SendTestAsync(admin, endpoint.Endpoint.Id);

        var attempts = await server.Webhooks.WaitForAsync(url, _ => true, count: 3);
        attempts.Select(attempt => attempt.Answer).ShouldBe([HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        attempts.Select(attempt => attempt.Id).ShouldAllBe(id => id == test.EventId.ToString());

        var delivery = await WaitForDeliveryAsync(admin, test.Id, WebhookDeliveryStatus.Succeeded);
        delivery.Attempts.ShouldBe(3);
        (await GetEndpointAsync(admin, endpoint.Endpoint.Id)).FailingSince.ShouldBeNull();
        await DeleteEndpointAsync(admin, endpoint.Endpoint.Id);
    }

    [Fact]
    public async Task DeliveryThatKeepsFailing_GivesUp_FlagsTheEndpoint_AndCanBeSentAgain()
    {
        using var admin = await server.CreateApiClientAsync();
        var url = server.Webhooks.NewEndpoint();
        server.Webhooks.AnswerWith(url, HttpStatusCode.InternalServerError);
        var endpoint = await CreateEndpointAsync(admin, url, enabled: false);

        var test = await SendTestAsync(admin, endpoint.Endpoint.Id);
        var failed = await WaitForDeliveryAsync(admin, test.Id, WebhookDeliveryStatus.Failed);
        failed.Attempts.ShouldBe(4);
        failed.ResponseStatusCode.ShouldBe(500);
        failed.ResponseBody.ShouldBe("not now");
        (await GetEndpointAsync(admin, endpoint.Endpoint.Id)).FailingSince.ShouldNotBeNull();

        server.Webhooks.AnswerWith(url, HttpStatusCode.OK);
        using var redelivered = await admin.Http.PostAsync($"/api/v1/webhooks/deliveries/{failed.Id}/redeliver");
        var succeeded = await WaitForDeliveryAsync(admin, failed.Id, WebhookDeliveryStatus.Succeeded);
        succeeded.Payload.GetProperty("type").GetString().ShouldBe(WebhookEventTypes.Test);
        (await GetEndpointAsync(admin, endpoint.Endpoint.Id)).FailingSince.ShouldBeNull();
        await DeleteEndpointAsync(admin, endpoint.Endpoint.Id);
    }

    [Fact]
    public async Task Endpoints_OnlyReceiveTheirEvents_AndNothingWhileDisabled()
    {
        using var admin = await server.CreateApiClientAsync();
        var organizationsOnly = server.Webhooks.NewEndpoint();
        var disabled = server.Webhooks.NewEndpoint();
        var everything = server.Webhooks.NewEndpoint();
        var first = await CreateEndpointAsync(admin, organizationsOnly, WebhookEventTypes.OrganizationCreated);
        var second = await CreateEndpointAsync(admin, disabled, WebhookEventTypes.UserCreated);
        var third = await CreateEndpointAsync(admin, everything);
        using var turnedOff = await admin.Http.SendJsonAsync(HttpMethod.Patch, $"/api/v1/webhooks/endpoints/{second.Endpoint.Id}", """{ "enabled": false }""");
        (await turnedOff.ReadAsync<WebhookEndpointResponse>()).Enabled.ShouldBeFalse();

        using var created = await admin.Http.PostJsonAsync("/api/v1/users", new CreateUserRequest { Email = $"filtered-{Guid.NewGuid():N}@example.com" });
        var user = await created.ReadAsync<UserResponse>();
        await server.Webhooks.WaitForAsync(everything, webhook => webhook.Body.Contains(user.Id.ToString(), StringComparison.Ordinal));

        server.Webhooks.ReceivedBy(organizationsOnly).ShouldNotContain(webhook => webhook.Body.Contains(user.Id.ToString(), StringComparison.Ordinal));
        // Other tests' users, created before it was turned off, may have reached it; this one may not.
        server.Webhooks.ReceivedBy(disabled).ShouldNotContain(webhook => webhook.Body.Contains(user.Id.ToString(), StringComparison.Ordinal));

        // Trying a disabled endpoint out still works.
        using var test = await admin.Http.PostAsync($"/api/v1/webhooks/endpoints/{second.Endpoint.Id}/test");
        await server.Webhooks.WaitForAsync(disabled, webhook => webhook.Body.Contains(WebhookEventTypes.Test, StringComparison.Ordinal));

        foreach (var endpoint in new[] { first, second, third })
        {
            await DeleteEndpointAsync(admin, endpoint.Endpoint.Id);
        }
    }

    [Fact]
    public async Task RotatedSecret_SignsWhatFollows()
    {
        using var admin = await server.CreateApiClientAsync();
        var url = server.Webhooks.NewEndpoint();
        var endpoint = await CreateEndpointAsync(admin, url, enabled: false);

        using var rotated = await admin.Http.PostAsync($"/api/v1/webhooks/endpoints/{endpoint.Endpoint.Id}/secret");
        var secret = (await rotated.ReadAsync<WebhookSecretResponse>()).Secret;
        var test = await SendTestAsync(admin, endpoint.Endpoint.Id);

        var received = (await server.Webhooks.WaitForAsync(url, webhook => webhook.Id == test.EventId.ToString())).Single();
        KimlikWebhook.TryVerify(received.Headers, received.Body, secret, out _).ShouldBeTrue();
        KimlikWebhook.TryVerify(received.Headers, received.Body, endpoint.Secret, out _).ShouldBeFalse();
        await DeleteEndpointAsync(admin, endpoint.Endpoint.Id);
    }

    [Fact]
    public async Task Endpoints_NeedSecureUrls_AndKnownEvents_AndThePermission()
    {
        using var admin = await server.CreateApiClientAsync();

        (await CreateProblemAsync(admin, new CreateWebhookEndpointRequest { Url = "http://example.com/hook" })).ShouldBe("webhook.invalid_url");
        (await CreateProblemAsync(admin, new CreateWebhookEndpointRequest { Url = "https://user:secret@example.com/hook" })).ShouldBe("webhook.invalid_url");
        (await CreateProblemAsync(admin, new CreateWebhookEndpointRequest { Url = "https://example.com/hook", EventTypes = ["user.teleported"] }))
            .ShouldBe("webhook.unknown_event_type");

        // Plain HTTP is fine on this machine, for development.
        using var local = await admin.Http.PostJsonAsync("/api/v1/webhooks/endpoints", new CreateWebhookEndpointRequest { Url = "http://localhost:5173/hooks", Enabled = false });
        local.StatusCode.ShouldBe(HttpStatusCode.Created);
        await DeleteEndpointAsync(admin, (await local.ReadAsync<CreatedWebhookEndpointResponse>()).Endpoint.Id);

        using var stranger = await server.CreateApiClientAsync(role: null);
        using var forbidden = await stranger.Http.GetAsync("/api/v1/webhooks/endpoints", CancellationToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<CreatedWebhookEndpointResponse> CreateEndpointAsync(ApiClient admin, string url, params string[] eventTypes) =>
        await CreateEndpointAsync(admin, url, enabled: true, eventTypes);

    private static async Task<CreatedWebhookEndpointResponse> CreateEndpointAsync(ApiClient admin, string url, bool enabled, params string[] eventTypes)
    {
        using var response = await admin.Http.PostJsonAsync(
            "/api/v1/webhooks/endpoints", new CreateWebhookEndpointRequest { Url = url, EventTypes = eventTypes, Enabled = enabled });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadAsync<CreatedWebhookEndpointResponse>();
        created.Secret.ShouldStartWith("whsec_");
        return created;
    }

    private static async Task<string?> CreateProblemAsync(ApiClient admin, CreateWebhookEndpointRequest request)
    {
        using var response = await admin.Http.PostJsonAsync("/api/v1/webhooks/endpoints", request);
        return await response.ReadProblemCodeAsync();
    }

    private static async Task<WebhookEndpointResponse> GetEndpointAsync(ApiClient admin, Guid id)
    {
        using var response = await admin.Http.GetAsync($"/api/v1/webhooks/endpoints/{id}", CancellationToken);
        return await response.ReadAsync<WebhookEndpointResponse>();
    }

    private static async Task DeleteEndpointAsync(ApiClient admin, Guid id)
    {
        using var response = await admin.Http.DeleteAsync($"/api/v1/webhooks/endpoints/{id}", CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<WebhookDeliveryResponse> SendTestAsync(ApiClient admin, Guid endpointId)
    {
        using var response = await admin.Http.PostAsync($"/api/v1/webhooks/endpoints/{endpointId}/test");
        return await response.ReadAsync<WebhookDeliveryResponse>();
    }

    private static async Task<WebhookDeliveryResponse> WaitForDeliveryAsync(ApiClient admin, Guid deliveryId, WebhookDeliveryStatus status)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            using var response = await admin.Http.GetAsync($"/api/v1/webhooks/deliveries/{deliveryId}", timeout.Token);
            if (await response.ReadAsync<WebhookDeliveryResponse>() is { } delivery && delivery.Status == status)
            {
                return delivery;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
