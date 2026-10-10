using System.Net;
using System.Security.Cryptography;
using Bunit;
using Kimlik.Admin.Components.Pages.Access;
using Kimlik.Admin.Components.Pages.Clients;
using Kimlik.Admin.Components.Pages.Integrations;
using Kimlik.Admin.Components.Pages.Organizations;
using Kimlik.Admin.Components.Pages.Plans;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Kimlik.Server.Tests.Organizations;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace Kimlik.Server.Tests.Admin;

/// <summary>The admin panel's other screens: each renders, and the changes they make land.</summary>
public sealed class AdminScreensTests(KimlikServerFixture server)
{
    [Theory]
    [InlineData("/admin", "Dashboard")]
    [InlineData("/admin/users", "Users")]
    [InlineData("/admin/organizations", "Organizations")]
    [InlineData("/admin/roles", "Roles")]
    [InlineData("/admin/permissions", "Permissions")]
    [InlineData("/admin/clients", "Clients")]
    [InlineData("/admin/api-resources", "API resources")]
    [InlineData("/admin/plans", "Plans")]
    [InlineData("/admin/subscriptions", "Subscriptions")]
    [InlineData("/admin/api-keys", "API keys")]
    [InlineData("/admin/webhooks", "Webhooks")]
    [InlineData("/admin/audit", "Audit log")]
    public async Task Screen_Renders(string path, string title)
    {
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);
        using var browser = new Browser(server);
        using var _ = await browser.SignInAsync(admin.Email, admin.Password);

        using var response = await browser.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Browser.ReadPageAsync(response)).Document.QuerySelector("h1")!.TextContent.Trim().ShouldBe(title);
    }

    [Fact]
    public async Task PermissionMatrix_GivesARoleAPermission()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var extra = await server.QueryDatabaseAsync(async context =>
        {
            var permission = Permission.Create($"reports{Guid.NewGuid():N}"[..20] + ":read", null, DateTimeOffset.UtcNow).Value;
            context.Permissions.Add(permission);
            await context.SaveChangesAsync();
            return permission.Key;
        });
        await using var admin = new AdminComponents(server, Guid.NewGuid());

        var page = admin.Render<Roles>();
        MudCheckBox<bool>? Box() => page.FindComponents<MudCheckBox<bool>>().Select(box => box.Instance).SingleOrDefault(box =>
            box.UserAttributes.TryGetValue("data-role", out var boxRole) && boxRole as string == role
            && box.UserAttributes.TryGetValue("data-permission", out var boxPermission) && boxPermission as string == extra);
        page.WaitForState(() => Box() is not null);
        await page.InvokeAsync(() => Box()!.ValueChanged.InvokeAsync(true));

        admin.WaitForNotification($"can now {extra}");
        var granted = await server.QueryDatabaseAsync(context => context.Roles.Where(candidate => candidate.Key == role)
            .SelectMany(candidate => candidate.Permissions)
            .Join(context.Permissions, link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Key)
            .ToListAsync());
        granted.ShouldBe([.. permissions, extra], ignoreOrder: true);
    }

    [Fact]
    public async Task Organization_InvitesSomeone()
    {
        var organization = await server.CreateOrganizationAsync();
        var email = $"invited-{Guid.NewGuid():N}@example.com";
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var page = admin.Render<OrganizationDetail>(parameters => parameters.Add(detail => detail.Id, organization.Id));

        page.WaitForElement("#invite").Click();
        var emailField = admin.Dialogs.WaitForElement("input[type=email]");
        emailField.Change(email);
        admin.Confirm("Send");

        admin.WaitForNotification("The invitation was sent.");
        (await server.QueryDatabaseAsync(context => context.Invitations.AnyAsync(invitation => invitation.OrganizationId == organization.Id && invitation.Email == email)))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task NewServiceClient_ShowsItsSecretOnce()
    {
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var clientId = $"worker-{Guid.NewGuid():N}"[..20];
        var page = admin.Render<Clients>();

        page.WaitForElement("button").Click();
        var fields = admin.Dialogs.WaitForElements("input");
        fields[0].Change(clientId);
        fields[1].Change("Worker");
        await page.InvokeAsync(() => admin.Dialogs.FindComponent<MudSelect<Kimlik.Contracts.Management.ClientType>>().Instance
            .ValueChanged.InvokeAsync(Kimlik.Contracts.Management.ClientType.Service));
        admin.Confirm("Create");

        var secret = admin.Dialogs.WaitForElement("#secret");
        secret.GetAttribute("value").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ServiceClient_SwitchesFromItsSecretToKeys()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        var id = await server.QueryDatabaseAsync(context => context.Applications.Where(application => application.ClientId == serviceClient.ClientId)
            .Select(application => application.Id).SingleAsync());
        using var key = RSA.Create(2048);
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var page = admin.Render<ClientDetail>(parameters => parameters.Add(detail => detail.Id, id));
        page.WaitForElement("#save-keys");

        await page.InvokeAsync(() => page.Find("#credentials textarea").Change(TestKeys.KeySet(key, "admin-1").ToJsonString()));
        await page.InvokeAsync(() => page.Find("#save-keys").Click());

        admin.WaitForNotification("The keys were saved.");
        page.WaitForAssertion(() => page.Find("#credentials").TextContent.ShouldContain("Authenticates with its keys"));
    }

    [Fact]
    public async Task NewSubscription_FindsSubscribers_OnlyWithTheRightToSeeThem()
    {
        await using var admin = new AdminComponents(
            server, Guid.NewGuid(), new HashSet<string>(StringComparer.Ordinal) { SystemPermissions.SubscriptionsRead, SystemPermissions.SubscriptionsWrite });
        var page = admin.Render<Subscriptions>();

        page.WaitForElement("button").Click();

        admin.Dialogs.WaitForAssertion(() => admin.Dialogs.Markup.ShouldContain($"Finding users takes {SystemPermissions.UsersRead}."));
        admin.Dialogs.FindComponent<MudAutocomplete<Kimlik.Contracts.Management.UserResponse>>().Instance.Disabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Subscription_GetsAddOnsAndOverrides()
    {
        using var api = await server.CreateApiClientAsync();
        var catalog = await Plans.AddOnCatalog.CreateAsync(api);
        var user = await server.CreateUserAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/subscriptions", new Kimlik.Contracts.Management.CreateSubscriptionRequest
        {
            SubscriberType = Kimlik.Contracts.Management.SubscriberType.User,
            SubscriberId = user.Id,
            Plan = catalog.Team,
        });
        var subscription = await created.ReadAsync<Kimlik.Contracts.Management.SubscriptionResponse>();
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var page = admin.Render<Subscriptions>();

        page.WaitForElement($"[data-extras='{subscription.Id}']").Click();
        await page.InvokeAsync(() => admin.Dialogs.FindComponents<MudNumericField<int>>()
            .Single(field => field.Instance.UserAttributes.TryGetValue("data-add-on", out var key) && key as string == catalog.ExtraSeats)
            .Instance.ValueChanged.InvokeAsync(3));
        await page.InvokeAsync(() => admin.Dialogs.Find("textarea#feature-overrides").Change($$"""{ "{{catalog.Seats}}": 40 }"""));
        await page.InvokeAsync(() => admin.Dialogs.Find("#save-extras").Click());

        admin.WaitForNotification("The add-ons and overrides were saved.");
        using var saved = await api.Http.GetAsync($"/api/v1/subscriptions/{subscription.Id}", Xunit.TestContext.Current.CancellationToken);
        var extras = await saved.ReadAsync<Kimlik.Contracts.Management.SubscriptionResponse>();
        extras.AddOns.ShouldBe(new Dictionary<string, int> { [catalog.ExtraSeats] = 3 });
        extras.FeatureOverrides[catalog.Seats].GetInt64().ShouldBe(40);
    }

    [Fact]
    public async Task WebhookEndpoint_SendsATestEvent()
    {
        var url = server.Webhooks.NewEndpoint();
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var endpointId = await server.WithServicesAsync(async services =>
        {
            var created = await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Kimlik.Application.Webhooks.CreateWebhookEndpointHandler>(services)
                .HandleAsync(new Kimlik.Contracts.Management.CreateWebhookEndpointRequest { Url = url, Enabled = false }, CancellationToken.None);
            return created.Value.Endpoint.Id;
        });
        var page = admin.Render<WebhookDetail>(parameters => parameters.Add(detail => detail.Id, endpointId));

        page.WaitForElement("#send-test").Click();

        await server.Webhooks.WaitForAsync(url, webhook => webhook.Body.Contains("webhook.test", StringComparison.Ordinal));
        admin.WaitForNotification("A test event is on its way.");
    }
}
