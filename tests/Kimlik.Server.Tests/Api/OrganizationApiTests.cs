using System.Net;
using System.Text.Json.Nodes;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Accounts;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Tests.Api;

public sealed class OrganizationApiTests(KimlikServerFixture server)
{
    private const string Organizations = "/api/v1/organizations";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Organization_CanBeCreatedRenamedAndDeleted()
    {
        using var api = await server.CreateApiClientAsync();
        var slug = NewSlug();

        using var created = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "Acme Labs", Slug = slug });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var organization = await created.ReadAsync<OrganizationResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{Organizations}/{organization.Id}");

        using var renamed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Organizations}/{organization.Id}", """{ "name": "Acme Research" }""");
        var updated = await renamed.ReadAsync<OrganizationResponse>();
        updated.Name.ShouldBe("Acme Research");
        updated.Slug.ShouldBe(slug);

        using var found = await api.Http.GetAsync($"{Organizations}?q={slug}", CancellationToken);
        (await found.ReadAsync<Page<OrganizationResponse>>()).Items.ShouldHaveSingleItem().Id.ShouldBe(organization.Id);

        using var deleted = await api.Http.DeleteAsync($"{Organizations}/{organization.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var gone = await api.Http.GetAsync($"{Organizations}/{organization.Id}", CancellationToken);
        (await gone.ReadProblemCodeAsync()).ShouldBe("organization.not_found");
    }

    [Fact]
    public async Task Picture_IsKeptOnlyWhenItIsAWebAddress()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(
            Organizations, new CreateOrganizationRequest { Name = "Acme", Slug = NewSlug(), PictureUrl = "https://example.com/acme.svg" });
        var organization = await created.ReadAsync<OrganizationResponse>();
        organization.PictureUrl.ShouldBe("https://example.com/acme.svg");

        using var relative = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Organizations}/{organization.Id}", """{ "pictureUrl": "/logo.png" }""");

        (await relative.ReadProblemCodeAsync()).ShouldBe("profile.invalid_picture_url");
    }

    [Fact]
    public async Task Metadata_IsSetOnCreation_AndReplacedOneByOne()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest
        {
            Name = "Acme",
            Slug = NewSlug(),
            PublicMetadata = new JsonObject { ["tier"] = "gold" },
            PrivateMetadata = new JsonObject { ["crmId"] = "acc_9" },
        });
        var organization = await created.ReadAsync<OrganizationResponse>();

        using var replaced = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Organizations}/{organization.Id}", """{ "publicMetadata": null }""");
        var updated = await replaced.ReadAsync<OrganizationResponse>();

        updated.PublicMetadata.ShouldBeEmpty();
        updated.PrivateMetadata["crmId"]!.GetValue<string>().ShouldBe("acc_9");
        using var tooLarge = await api.Http.SendJsonAsync(
            HttpMethod.Patch, $"{Organizations}/{organization.Id}", $$"""{ "privateMetadata": { "notes": "{{new string('x', 9000)}}" } }""");
        (await tooLarge.ReadProblemCodeAsync()).ShouldBe("metadata.too_large");
    }

    [Theory]
    [InlineData("Acme", "organization.invalid_slug")]
    [InlineData("0199c3a1-0f2e-7d3c-8b4a-5e6f70819203", "organization.invalid_slug")]
    public async Task CreateOrganization_WithInvalidSlug_IsRejected(string slug, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "Acme", Slug = slug });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task Slugs_AreUnique()
    {
        using var api = await server.CreateApiClientAsync();
        var slug = NewSlug();
        using var first = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "First", Slug = slug });
        using var other = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "Other", Slug = NewSlug() });
        var otherId = (await other.ReadAsync<OrganizationResponse>()).Id;

        using var duplicate = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "Second", Slug = slug });
        using var renamed = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Organizations}/{otherId}", $$"""{ "slug": "{{slug}}" }""");

        (await duplicate.ReadProblemCodeAsync()).ShouldBe("organization.slug_taken");
        (await renamed.ReadProblemCodeAsync()).ShouldBe("organization.slug_taken");
    }

    [Fact]
    public async Task Members_HoldOrganizationRoles()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await CreateOrganizationAsync(api);
        var user = await server.CreateUserAsync();
        var owner = await CreateOrganizationRoleAsync(api);

        using var added = await api.Http.PostJsonAsync($"{Organizations}/{organization.Id}/members", new AddMemberRequest { UserId = user.Id, Roles = [owner] });
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var member = await added.ReadAsync<MemberResponse>();
        member.Email.ShouldBe(user.Email);
        member.Name.ShouldBe("Ada Lovelace");
        member.Roles.ShouldBe([owner]);

        using var again = await api.Http.PostJsonAsync($"{Organizations}/{organization.Id}/members", new AddMemberRequest { UserId = user.Id });
        (await again.ReadProblemCodeAsync()).ShouldBe("organization.already_member");

        using var promoted = await api.Http.PutJsonAsync(
            $"{Organizations}/{organization.Id}/members/{user.Id}/roles", new SetRolesRequest { Roles = [owner, SystemRoles.OrganizationAdmin] });
        (await promoted.ReadAsync<MemberResponse>()).Roles.ShouldBe([owner, SystemRoles.OrganizationAdmin], ignoreOrder: true);

        using var global = await api.Http.PutJsonAsync($"{Organizations}/{organization.Id}/members/{user.Id}/roles", new SetRolesRequest { Roles = [SystemRoles.Admin] });
        (await global.ReadProblemCodeAsync()).ShouldBe("organization.global_role_not_assignable");

        using var listed = await api.Http.GetAsync($"{Organizations}/{organization.Id}/members", CancellationToken);
        (await listed.ReadAsync<Page<MemberResponse>>()).Items.ShouldHaveSingleItem().UserId.ShouldBe(user.Id);

        using var removed = await api.Http.DeleteAsync($"{Organizations}/{organization.Id}/members/{user.Id}", CancellationToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var gone = await api.Http.DeleteAsync($"{Organizations}/{organization.Id}/members/{user.Id}", CancellationToken);
        (await gone.ReadProblemCodeAsync()).ShouldBe("organization.member_not_found");
    }

    [Fact]
    public async Task OrganizationRoles_CannotHoldInstallationWideSystemPermissions()
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync("/api/v1/roles", new CreateRoleRequest
        {
            Key = $"org-root-{Guid.NewGuid():N}",
            Name = "Root",
            Scope = RoleScope.Organization,
            Permissions = [SystemPermissions.UsersWrite],
        });

        (await response.ReadProblemCodeAsync()).ShouldBe("access.global_permission_in_organization_role");
    }

    [Fact]
    public async Task OrganizationEvents_AreAuditedUnderTheOrganization()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await CreateOrganizationAsync(api);
        var user = await server.CreateUserAsync();
        using var added = await api.Http.PostJsonAsync($"{Organizations}/{organization.Id}/members", new AddMemberRequest { UserId = user.Id });

        using var trail = await api.Http.GetAsync($"/api/v1/audit-events?organizationId={organization.Id}", CancellationToken);

        (await trail.ReadAsync<Page<AuditEventResponse>>()).Items.Select(auditEvent => auditEvent.Action)
            .ShouldBe(["membership.created", "organization.created"]);
    }

    private static async Task<OrganizationResponse> CreateOrganizationAsync(ApiClient api)
    {
        using var created = await api.Http.PostJsonAsync(Organizations, new CreateOrganizationRequest { Name = "Acme", Slug = NewSlug() });
        return await created.ReadAsync<OrganizationResponse>();
    }

    private static async Task<string> CreateOrganizationRoleAsync(ApiClient api)
    {
        var key = $"owner-{Guid.NewGuid():N}";
        using var created = await api.Http.PostJsonAsync("/api/v1/roles", new CreateRoleRequest
        {
            Key = key,
            Name = "Owner",
            Scope = RoleScope.Organization,
            Permissions = [SystemPermissions.OrganizationMembersWrite],
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return key;
    }

    private static string NewSlug() => $"acme-{Guid.NewGuid():N}"[..20];
}
