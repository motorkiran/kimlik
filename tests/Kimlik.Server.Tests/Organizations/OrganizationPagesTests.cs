using System.Net;
using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;
using Kimlik.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Organizations;

/// <summary>Organizations on the hosted account pages, managed as the member's organization roles allow.</summary>
public sealed class OrganizationPagesTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Member_CreatesAnOrganization_AndInvitesSomeone_WhoJoins()
    {
        var founder = await server.CreateUserAsync();
        var invitee = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(founder.Email, founder.Password);

        var slug = $"acme-{Guid.NewGuid():N}"[..20];
        var id = await CreateAsync(browser, slug);
        var page = await browser.GetPageAsync($"/account/organizations/{id}");
        page.Text.ShouldContain($"Your roles: {SystemRoles.OrganizationAdmin}");
        var invited = await Browser.ReadPageAsync(await browser.SubmitAsync(page, new Dictionary<string, string> { ["Invite.Email"] = invitee.Email }, "#invite"));
        invited.Text.ShouldContain($"We sent an invitation to {invitee.Email}.");
        invited.Document.QuerySelector("#invitations")!.TextContent.ShouldContain(invitee.Email);
        await server.Emails.WaitForAsync(invitee.Email, "invited to join");

        using var inviteeBrowser = new Browser(server);
        using var inviteeSignIn = await inviteeBrowser.SignInAsync(invitee.Email, invitee.Password);
        var invitations = await inviteeBrowser.GetPageAsync("/account/organizations");
        using var joined = await inviteeBrowser.SubmitAsync(invitations, formSelector: "#invitations form[action*='Accept']");

        joined.Headers.Location!.OriginalString.ShouldBe($"/account/organizations/{id}");
        (await inviteeBrowser.GetPageAsync("/account/organizations")).Document.QuerySelector("#organizations")!.TextContent.ShouldContain("Acme");
        (await IsMemberAsync(id, invitee.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Administrator_ChangesAMembersRoles_AndRemovesThem()
    {
        var administrator = await server.CreateUserAsync();
        var member = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);
        var id = await CreateAsync(browser, $"acme-{Guid.NewGuid():N}"[..20]);
        var role = await AddMemberAsync(id, member.Id);

        var memberPage = await browser.GetPageAsync($"/account/organizations/{id}/members/{member.Id}");
        var saved = await Browser.ReadPageAsync(await browser.SubmitAsync(memberPage, new Dictionary<string, string> { ["Selected"] = role }, "#roles"));
        saved.Text.ShouldContain("The roles have been saved.");
        (await RolesOfAsync(id, member.Id)).ShouldBe([role]);

        using var removed = await browser.SubmitAsync(saved, formSelector: "#remove-member");
        removed.Headers.Location!.OriginalString.ShouldBe($"/account/organizations/{id}");
        (await IsMemberAsync(id, member.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task MemberWithoutTheRoles_SeesNoManagement_AndCannotManage()
    {
        var user = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(user.Id);
        var outsider = $"outsider-{Guid.NewGuid():N}@example.com";
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password);

        var page = await browser.GetPageAsync($"/account/organizations/{organization.Id}");
        page.Document.QuerySelectorAll("#members, #invite, #settings, #delete-organization").ShouldBeEmpty();
        page.Document.QuerySelector("#leave").ShouldNotBeNull();

        var refused = await Browser.ReadPageAsync(await browser.SubmitAsync(
            page, new Dictionary<string, string> { ["Invite.Email"] = outsider }, "#leave", action: "?handler=Invite"));
        refused.Text.ShouldContain("Your roles in the organization do not allow this.");
        (await server.QueryDatabaseAsync(context => context.Invitations.AnyAsync(invitation => invitation.Email == outsider))).ShouldBeFalse();

        using var memberPage = await browser.GetAsync($"/account/organizations/{organization.Id}/members/{user.Id}");
        memberPage.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var otherOrganization = await browser.GetAsync($"/account/organizations/{(await server.CreateOrganizationAsync()).Id}");
        otherOrganization.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LastAdministrator_CannotLeave_ButCanRenameAndDelete()
    {
        var administrator = await server.CreateUserAsync();
        var member = await server.CreateUserAsync();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(administrator.Email, administrator.Password);
        var slug = $"acme-{Guid.NewGuid():N}"[..20];
        var id = await CreateAsync(browser, slug);
        await AddMemberAsync(id, member.Id);

        var page = await browser.GetPageAsync($"/account/organizations/{id}");
        (await Browser.ReadPageAsync(await browser.SubmitAsync(page, formSelector: "#leave")))
            .Text.ShouldContain("Someone else must be able to manage the members before you leave.");

        var renamed = await Browser.ReadPageAsync(await browser.SubmitAsync(page, new Dictionary<string, string> { ["Settings.Name"] = "Acme Labs" }, "#settings"));
        renamed.Text.ShouldContain("The organization has been saved.");
        renamed.Document.QuerySelector("h1")!.TextContent.ShouldBe("Acme Labs");

        var notConfirmed = await Browser.ReadPageAsync(await browser.SubmitAsync(renamed, new Dictionary<string, string> { ["confirmation"] = "acme" }, "#delete-organization"));
        notConfirmed.Text.ShouldContain("Type the organization's short name to delete it.");
        using var deleted = await browser.SubmitAsync(renamed, new Dictionary<string, string> { ["confirmation"] = slug }, "#delete-organization");
        deleted.Headers.Location!.OriginalString.ShouldBe("/account/organizations");
        (await server.QueryDatabaseAsync(context => context.Organizations.AnyAsync(organization => organization.Id == id))).ShouldBeFalse();
    }

    /// <summary>Creates an organization named Acme on the account pages, and returns its ID.</summary>
    private static async Task<Guid> CreateAsync(Browser browser, string slug)
    {
        var page = await browser.GetPageAsync("/account/organizations");
        using var created = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Name"] = "Acme", ["Input.Slug"] = slug }, "#create-organization");
        var location = created.Headers.Location?.OriginalString ?? throw new InvalidOperationException($"Expected a redirect, got {(int)created.StatusCode}.");
        location.ShouldStartWith("/account/organizations/");
        return Guid.Parse(location["/account/organizations/".Length..]);
    }

    /// <summary>Makes the user a member without roles, and returns the key of a new organization role they could hold.</summary>
    private Task<string> AddMemberAsync(Guid organizationId, Guid userId) =>
        server.QueryDatabaseAsync(async context =>
        {
            var now = DateTimeOffset.UtcNow;
            var role = Role.Create($"viewer-{Guid.NewGuid():N}"[..20], "Viewer", null, RoleScope.Organization, now).Value;
            context.Roles.Add(role);
            context.Memberships.Add(Membership.Create(organizationId, userId, now));
            await context.SaveChangesAsync();
            return role.Key;
        });

    private Task<bool> IsMemberAsync(Guid organizationId, Guid userId) =>
        server.QueryDatabaseAsync(context => context.Memberships.AnyAsync(membership => membership.OrganizationId == organizationId && membership.UserId == userId));

    private Task<List<string>> RolesOfAsync(Guid organizationId, Guid userId) =>
        server.QueryDatabaseAsync(context => context.Memberships
            .Where(membership => membership.OrganizationId == organizationId && membership.UserId == userId)
            .SelectMany(membership => membership.Roles)
            .Join(context.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Key)
            .ToListAsync());
}
