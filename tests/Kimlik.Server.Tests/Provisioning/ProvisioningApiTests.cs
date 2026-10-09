using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Tests.Provisioning;

public sealed class ProvisioningApiTests(KimlikServerFixture server)
{
    private const string Provisioning = "/api/v1/provisioning";
    private const string WorkerSecret = "a provisioned worker secret";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Document_CreatesTheModel_ThenOnlyAppliesChanges()
    {
        using var api = await server.CreateApiClientAsync();
        var model = new TestModel();

        using var applied = await api.Http.PostJsonAsync(Provisioning, model.Document());
        applied.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await applied.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 6, Updated: 0, Unchanged: 0));

        using var http = server.CreateClient();
        var token = new JsonWebToken(await http.RequestClientCredentialsTokenAsync(new TestClient(model.Worker, WorkerSecret), model.Scope));
        token.Audiences.ShouldBe([model.Scope]);
        JsonSerializer.Deserialize<string[]>(token.GetPayloadValue<JsonElement>(KimlikClaimTypes.Permissions).GetRawText()).ShouldBe([model.Read]);

        using var again = await api.Http.PostJsonAsync(Provisioning, model.Document());
        (await again.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 0, Updated: 0, Unchanged: 6));

        using var changed = await api.Http.PostJsonAsync(Provisioning, model.Document(rolePermissions: [model.Read, model.Write], readDescription: "Read reports"));
        (await changed.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 0, Updated: 2, Unchanged: 4));
    }

    [Fact]
    public async Task Document_IsAppliedAllOrNothing()
    {
        using var api = await server.CreateApiClientAsync();
        var model = new TestModel();
        var document = model.Document() with
        {
            Roles = [new ProvisionedRole { Key = model.Role, Name = "Reporter", Permissions = ["no.such:permission"] }],
        };

        using var response = await api.Http.PostJsonAsync(Provisioning, document);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("access.unknown_permission");
        problem.GetProperty("detail").GetString().ShouldStartWith($"roles[0] '{model.Role}'");
        using var permissions = await api.Http.GetAsync($"/api/v1/permissions?q={model.Read}", CancellationToken);
        (await permissions.ReadAsync<Page<PermissionResponse>>()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task FixedProperties_CannotChange()
    {
        using var api = await server.CreateApiClientAsync();
        var model = new TestModel();
        using var applied = await api.Http.PostJsonAsync(Provisioning, model.Document());

        using var response = await api.Http.PostJsonAsync(Provisioning, model.Document() with
        {
            Roles = [new ProvisionedRole { Key = model.Role, Name = "Reporter", Scope = RoleScope.Organization }],
        });

        (await response.ReadProblemCodeAsync()).ShouldBe("provisioning.fixed_property");
    }

    [Fact]
    public async Task Export_DescribesWhatWasProvisioned_AndAppliesBackUnchanged()
    {
        using var api = await server.CreateApiClientAsync();
        var model = new TestModel();
        using var applied = await api.Http.PostJsonAsync(Provisioning, model.Document());

        using var exported = await api.Http.GetAsync(Provisioning, CancellationToken);
        var document = await exported.ReadAsync<ProvisioningDocument>();

        document.Permissions!.ShouldNotContain(permission => permission.Key.StartsWith("kimlik.", StringComparison.Ordinal));
        document.Roles!.ShouldNotContain(role => role.Key == SystemRoles.Admin);
        document.ApiResources!.ShouldNotContain(resource => resource.Scope == KimlikScopes.Api);
        var worker = document.Clients!.Single(client => client.ClientId == model.Worker);
        worker.ClientSecret.ShouldBeNull();
        worker.Roles.ShouldBe([model.Role]);

        using var reapplied = await api.Http.PostJsonAsync(Provisioning, new ProvisioningDocument
        {
            Permissions = [.. document.Permissions!.Where(permission => permission.Key == model.Read || permission.Key == model.Write)],
            Roles = [.. document.Roles!.Where(role => role.Key == model.Role)],
            ApiResources = [.. document.ApiResources!.Where(resource => resource.Scope == model.Scope)],
            Clients = [.. document.Clients!.Where(client => client.ClientId == model.Worker || client.ClientId == model.Web)],
        });
        (await reapplied.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 0, Updated: 0, Unchanged: 6));
    }

    [Fact]
    public async Task ClientKeysAndPushedAuthorization_AreProvisioned_AndExportedBackUnchanged()
    {
        using var api = await server.CreateApiClientAsync();
        using var key = RSA.Create(2048);
        var model = new TestModel();
        var declared = model.Document();
        declared = declared with
        {
            Clients =
            [
                declared.Clients![0] with { ClientSecret = null, JsonWebKeySet = TestKeys.KeySet(key, "worker-1") },
                declared.Clients[1] with { RequirePushedAuthorization = true },
            ],
        };

        using var applied = await api.Http.PostJsonAsync(Provisioning, declared);
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync(CancellationToken));
        using var exported = await api.Http.GetAsync(Provisioning, CancellationToken);
        var clients = (await exported.ReadAsync<ProvisioningDocument>()).Clients!;

        clients.Single(client => client.ClientId == model.Worker).JsonWebKeySet!["keys"]![0]!["kid"]!.GetValue<string>().ShouldBe("worker-1");
        clients.Single(client => client.ClientId == model.Web).RequirePushedAuthorization.ShouldBeTrue();
        using var reapplied = await api.Http.PostJsonAsync(Provisioning, declared);
        (await reapplied.ReadAsync<ProvisioningResult>()).ShouldBe(new ProvisioningResult(Created: 0, Updated: 0, Unchanged: 6));
    }

    [Fact]
    public async Task Document_StaysWithinTheCallersOwnAccess()
    {
        using var catalogManager = await server.CreateApiClientAsync(
            await server.CreateRoleWithAsync(SystemPermissions.RolesWrite, SystemPermissions.ClientsWrite, SystemPermissions.PlansWrite));
        var model = new TestModel();

        using var escalated = await catalogManager.Http.PostJsonAsync(Provisioning, new ProvisioningDocument
        {
            Roles = [new ProvisionedRole { Key = model.Role, Name = "Support", Permissions = [SystemPermissions.UsersWrite] }],
        });

        escalated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalated.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
    }

    [Fact]
    public async Task Applying_RequiresTheRightToChangeEverythingItCovers()
    {
        using var roleManager = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.RolesWrite, SystemPermissions.ClientsWrite));

        using var response = await roleManager.Http.PostJsonAsync(Provisioning, new ProvisioningDocument());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Two permissions, a role, an API resource, a service client and a web app, all with unique names.</summary>
    private sealed class TestModel
    {
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

        public string Read => $"reports{_suffix}:read";

        public string Write => $"reports{_suffix}:write";

        public string Role => $"reporter-{_suffix}";

        public string Scope => $"reports-{_suffix}";

        public string Worker => $"report-worker-{_suffix}";

        public string Web => $"reports-web-{_suffix}";

        public ProvisioningDocument Document(IReadOnlyList<string>? rolePermissions = null, string? readDescription = null) => new()
        {
            Permissions =
            [
                new ProvisionedPermission { Key = Read, Description = readDescription },
                new ProvisionedPermission { Key = Write },
            ],
            Roles = [new ProvisionedRole { Key = Role, Name = "Reporter", Permissions = rolePermissions ?? [Read] }],
            ApiResources = [new ProvisionedApiResource { Scope = Scope, DisplayName = "Read your reports" }],
            Clients =
            [
                new ProvisionedClient
                {
                    ClientId = Worker,
                    DisplayName = "Report worker",
                    Type = ClientType.Service,
                    Scopes = [Scope],
                    Roles = [Role],
                    ClientSecret = WorkerSecret,
                },
                new ProvisionedClient
                {
                    ClientId = Web,
                    DisplayName = "Reports",
                    Type = ClientType.Spa,
                    RedirectUris = ["http://localhost:3000/callback"],
                    Scopes = ["openid", "profile", Scope],
                },
            ],
        };
    }
}
