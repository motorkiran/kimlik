using Bunit;
using Kimlik.Admin.Components.Pages.Users;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Admin;

/// <summary>The user pages of the admin panel, clicked through with bUnit.</summary>
public sealed class UserPagesTests(KimlikServerFixture server)
{
    [Fact]
    public async Task Users_AreListed_AndFoundBySearch()
    {
        var user = await server.CreateUserAsync();
        await using var admin = new AdminComponents(server, Guid.NewGuid());

        var page = admin.Render<Users>();
        page.Find("input").Input(user.Email);

        page.WaitForAssertion(() => page.FindAll("#users tbody tr").ShouldHaveSingleItem().TextContent.ShouldContain(user.Email));
    }

    [Fact]
    public async Task Administrator_SuspendsAndReactivatesAUser_AsThemselves()
    {
        var administrator = await server.CreateUserAsync();
        var user = await server.CreateUserAsync();
        await using var admin = new AdminComponents(server, administrator.Id);

        var page = admin.Render<UserDetail>(parameters => parameters.Add(detail => detail.Id, user.Id));
        page.WaitForElement("#suspend").Click();
        admin.Confirm("Suspend");

        admin.WaitForNotification("The user was suspended.");
        page.WaitForElement("#reactivate");
        var actor = await server.QueryDatabaseAsync(context => context.AuditEvents
            .Where(auditEvent => auditEvent.Action == AuditActions.UserSuspended && auditEvent.SubjectId == user.Id.ToString())
            .Select(auditEvent => auditEvent.ActorId)
            .SingleAsync());
        actor.ShouldBe(administrator.Id.ToString());

        page.Find("#reactivate").Click();
        page.WaitForElement("#suspend");
    }

    [Fact]
    public async Task Administrator_GivesAUserARole()
    {
        var (role, _) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await using var admin = new AdminComponents(server, Guid.NewGuid());
        var page = admin.Render<UserDetail>(parameters => parameters.Add(detail => detail.Id, user.Id));
        page.WaitForElement("#save-roles");

        var select = page.FindComponent<MudBlazor.MudSelect<string>>().Instance;
        await page.InvokeAsync(() => select.SelectedValuesChanged.InvokeAsync([role]));
        page.Find("#save-roles").Click();

        admin.WaitForNotification("The roles were saved.");
        var roles = await server.QueryDatabaseAsync(context => context.UserRoles.Where(assignment => assignment.UserId == user.Id)
            .Join(context.Roles, assignment => assignment.RoleId, candidate => candidate.Id, (_, candidate) => candidate.Key).ToListAsync());
        roles.ShouldBe([role]);
    }

    [Fact]
    public async Task ReadOnlyAdministrator_SeesNoActions()
    {
        var user = await server.CreateUserAsync();
        await using var admin = new AdminComponents(server, Guid.NewGuid(), new HashSet<string> { SystemPermissions.UsersRead });

        var page = admin.Render<UserDetail>(parameters => parameters.Add(detail => detail.Id, user.Id));

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe(user.Email));
        page.FindAll("#suspend, #delete, #save-profile, #save-roles").ShouldBeEmpty();
    }
}
