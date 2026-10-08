using System.Net;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using RoleScope = Kimlik.Contracts.Management.RoleScope;

namespace Kimlik.Server.Tests.Organizations;

/// <summary>The Account API, called with the signed-in user's own access token.</summary>
public sealed class AccountApiTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_CreatesAnOrganization_AndAdministersIt()
    {
        using var signedIn = await SignedInUserAsync();
        var (user, me) = (signedIn.User, signedIn.Api);

        using var created = await me.PostJsonAsync("/api/v1/me/organizations", new CreateOrganizationRequest { Name = "Acme", Slug = NewSlug() });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var organization = await created.ReadAsync<OrganizationResponse>();

        using var listed = await me.GetAsync("/api/v1/me/organizations", CancellationToken);
        var mine = (await listed.ReadAsync<List<MyOrganizationResponse>>()).ShouldHaveSingleItem();
        mine.Id.ShouldBe(organization.Id);
        mine.Roles.ShouldBe([SystemRoles.OrganizationAdmin]);
        mine.Permissions.ShouldBe(SystemPermissions.Organization.Keys, ignoreOrder: true);

        using var renamed = await me.SendJsonAsync(HttpMethod.Patch, $"/api/v1/me/organizations/{organization.Id}", """{ "name": "Acme Labs" }""");
        (await renamed.ReadAsync<OrganizationResponse>()).Name.ShouldBe("Acme Labs");

        using var account = await me.GetAsync("/api/v1/me", CancellationToken);
        (await account.ReadAsync<UserResponse>()).Id.ShouldBe(user.Id);
    }

    [Fact]
    public async Task OrganizationAdmin_InvitesSomeone_WhoAcceptsThroughTheApi()
    {
        using var adminUser = await SignedInUserAsync();
        var admin = adminUser.Api;
        var organization = await CreateOrganizationAsync(admin);
        var memberRole = await CreateOrganizationRoleAsync();
        using var inviteeUser = await SignedInUserAsync();
        var (invitee, inviteeApi) = (inviteeUser.User, inviteeUser.Api);

        using var invited = await admin.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/invitations", new CreateInvitationRequest { Email = invitee.Email, Roles = [memberRole] });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var pending = await inviteeApi.GetAsync("/api/v1/me/invitations", CancellationToken);
        var invitation = (await pending.ReadAsync<List<MyInvitationResponse>>()).ShouldHaveSingleItem();
        invitation.OrganizationName.ShouldBe("Acme");
        invitation.Roles.ShouldBe([memberRole]);

        using var accepted = await inviteeApi.PostAsync($"/api/v1/me/invitations/{invitation.Id}/accept");
        accepted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var members = await admin.GetAsync($"/api/v1/me/organizations/{organization.Id}/members", CancellationToken);
        (await members.ReadAsync<Page<MemberResponse>>()).Items.Select(member => member.UserId).ShouldContain(invitee.Id);
    }

    [Fact]
    public async Task Members_ManageOnlyWhatTheirRolesAllow()
    {
        using var adminUser = await SignedInUserAsync();
        var admin = adminUser.Api;
        var organization = await CreateOrganizationAsync(admin);
        using var memberUser = await SignedInUserAsync();
        var (member, memberApi) = (memberUser.User, memberUser.Api);
        using var outsider = await SignedInUserAsync();
        var outsiderApi = outsider.Api;
        await AddMemberAsync(organization.Id, member.Id, await CreateOrganizationRoleAsync());

        using var refused = await memberApi.GetAsync($"/api/v1/me/organizations/{organization.Id}/members", CancellationToken);
        using var hidden = await outsiderApi.GetAsync($"/api/v1/me/organizations/{organization.Id}/members", CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemCodeAsync()).ShouldBe("organization.missing_permission");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await hidden.ReadProblemCodeAsync()).ShouldBe("organization.not_found");
    }

    [Fact]
    public async Task MembersWhoManageMembers_CannotOutrankThemselves()
    {
        using var creatorUser = await SignedInUserAsync();
        var (creator, admin) = (creatorUser.User, creatorUser.Api);
        var organization = await CreateOrganizationAsync(admin);
        using var managerUser = await SignedInUserAsync();
        var (manager, managerApi) = (managerUser.User, managerUser.Api);
        using var otherUser = await SignedInUserAsync();
        var other = otherUser.User;
        await AddMemberAsync(organization.Id, manager.Id, await CreateOrganizationRoleAsync(SystemPermissions.OrganizationMembersRead, SystemPermissions.OrganizationMembersWrite));
        await AddMemberAsync(organization.Id, other.Id, await CreateOrganizationRoleAsync());

        using var promoted = await managerApi.PutJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/members/{other.Id}/roles", new SetRolesRequest { Roles = [SystemRoles.OrganizationAdmin] });
        using var removed = await managerApi.DeleteAsync($"/api/v1/me/organizations/{organization.Id}/members/{creator.Id}", CancellationToken);
        using var removedOther = await managerApi.DeleteAsync($"/api/v1/me/organizations/{organization.Id}/members/{other.Id}", CancellationToken);

        (await promoted.ReadProblemCodeAsync()).ShouldBe("organization.privilege_escalation");
        (await removed.ReadProblemCodeAsync()).ShouldBe("organization.privilege_escalation");
        removedOther.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LastAdministrator_CannotLeaveOthersBehind()
    {
        using var adminUser = await SignedInUserAsync();
        var admin = adminUser.Api;
        var organization = await CreateOrganizationAsync(admin);
        using var memberUser = await SignedInUserAsync();
        var member = memberUser.User;
        await AddMemberAsync(organization.Id, member.Id, await CreateOrganizationRoleAsync());

        using var refused = await admin.DeleteAsync($"/api/v1/me/organizations/{organization.Id}/membership", CancellationToken);
        (await refused.ReadProblemCodeAsync()).ShouldBe("organization.last_administrator");

        using var promoted = await admin.PutJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/members/{member.Id}/roles", new SetRolesRequest { Roles = [SystemRoles.OrganizationAdmin] });
        using var left = await admin.DeleteAsync($"/api/v1/me/organizations/{organization.Id}/membership", CancellationToken);
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ServiceClient_HasNoAccount()
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.GetAsync("/api/v1/me", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<SignedInUser> SignedInUserAsync()
    {
        var user = await server.CreateUserAsync();
        return new SignedInUser(user, server.WithToken(await server.UserAccessTokenAsync(user)));
    }

    private static async Task<OrganizationResponse> CreateOrganizationAsync(HttpClient me)
    {
        using var created = await me.PostJsonAsync("/api/v1/me/organizations", new CreateOrganizationRequest { Name = "Acme", Slug = NewSlug() });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.ReadAsync<OrganizationResponse>();
    }

    private async Task<string> CreateOrganizationRoleAsync(params string[] permissions)
    {
        using var api = await server.CreateApiClientAsync();
        var key = $"member-{Guid.NewGuid():N}";
        using var created = await api.Http.PostJsonAsync("/api/v1/roles", new CreateRoleRequest { Key = key, Name = "Member", Scope = RoleScope.Organization, Permissions = permissions });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return key;
    }

    private async Task AddMemberAsync(Guid organizationId, Guid userId, string role)
    {
        using var api = await server.CreateApiClientAsync();
        using var added = await api.Http.PostJsonAsync($"/api/v1/organizations/{organizationId}/members", new AddMemberRequest { UserId = userId, Roles = [role] });
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static string NewSlug() => $"acme-{Guid.NewGuid():N}"[..20];

    /// <summary>A user and an HTTP client that calls Kimlik's API with the user's access token.</summary>
    private sealed record SignedInUser(TestUser User, HttpClient Api) : IDisposable
    {
        public void Dispose() => Api.Dispose();
    }
}
