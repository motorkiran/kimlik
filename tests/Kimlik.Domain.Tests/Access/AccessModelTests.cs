using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Tests.Access;

public sealed class AccessModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("invoices:read", true)]
    [InlineData("projects.members:invite", true)]
    [InlineData("api_keys:create", true)]
    [InlineData("Invoices:read", false)]
    [InlineData("invoices", false)]
    [InlineData("invoices:", false)]
    [InlineData(":read", false)]
    [InlineData("invoices:read:all", false)]
    [InlineData("1invoices:read", false)]
    public void PermissionKeys_FollowResourceActionFormat(string key, bool valid)
    {
        AccessKeys.IsValidPermissionKey(key).ShouldBe(valid);
    }

    [Fact]
    public void ApplicationPermissions_CannotUseTheSystemNamespace()
    {
        Permission.Create("kimlik.users:read", null, Now).Error.ShouldBe(AccessErrors.ReservedKey);
    }

    [Fact]
    public void ApplicationRoles_CannotUseTheSystemPrefix()
    {
        Role.Create("kimlik-auditor", "Auditor", null, RoleScope.Global, Now).Error.ShouldBe(AccessErrors.ReservedKey);
    }

    [Fact]
    public void SetPermissions_ReplacesTheWholeSet()
    {
        var read = Permission.Create("invoices:read", null, Now).Value;
        var write = Permission.Create("invoices:write", null, Now).Value;
        var export = Permission.Create("invoices:export", null, Now).Value;
        var role = Role.Create("accountant", "Accountant", null, RoleScope.Organization, Now).Value;

        role.SetPermissions([read, write], Now).IsSuccess.ShouldBeTrue();
        role.SetPermissions([write, export], Now).IsSuccess.ShouldBeTrue();

        role.Permissions.Select(link => link.PermissionId).ShouldBe([write.Id, export.Id], ignoreOrder: true);
    }

    [Fact]
    public void SystemDefinitions_AreReadOnly()
    {
        var role = Role.CreateSystem(SystemRoles.Admin, "Administrator", "Everything.", Now);
        var permission = Permission.CreateSystem(SystemPermissions.UsersRead, "View users.", Now);

        role.Update("Renamed", null, Now).Error.ShouldBe(AccessErrors.SystemDefinitionReadOnly);
        role.SetPermissions([], Now).Error.ShouldBe(AccessErrors.SystemDefinitionReadOnly);
        permission.Describe("Changed").Error.ShouldBe(AccessErrors.SystemDefinitionReadOnly);
    }

    [Fact]
    public void Descriptions_LongerThanTheLimit_AreRejectedNotTruncated()
    {
        var result = Permission.Create("invoices:read", new string('x', Permission.DescriptionMaxLength + 1), Now);

        result.Error.ShouldBe(AccessErrors.InvalidDescription);
    }

    [Fact]
    public void Results_OfFailedOperations_HaveNoValue()
    {
        Result<Role> result = AccessErrors.InvalidRoleKey;

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
