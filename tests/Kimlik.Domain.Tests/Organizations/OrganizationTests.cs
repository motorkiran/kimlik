using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;

namespace Kimlik.Domain.Tests.Organizations;

public sealed class OrganizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-labs", true)]
    [InlineData("a", true)]
    [InlineData("42", true)]
    [InlineData("Acme", false)]
    [InlineData("-acme", false)]
    [InlineData("acme-", false)]
    [InlineData("acme labs", false)]
    [InlineData("şirket", false)]
    [InlineData("0199c3a10f2e7d3c8b4a5e6f70819203", false)]
    public void Slugs_AreUrlFriendly_AndNeverReadAsAnId(string slug, bool valid)
    {
        Organization.IsValidSlug(slug).ShouldBe(valid);
    }

    [Fact]
    public void Create_TrimsTheName_AndRequiresOne()
    {
        Organization.Create("  Acme Labs ", "acme-labs", requireMfa: false, Now).Value.Name.ShouldBe("Acme Labs");
        Organization.Create("   ", "acme", requireMfa: false, Now).Error.ShouldBe(OrganizationErrors.InvalidName);
    }

    [Fact]
    public void Members_HoldOnlyOrganizationRoles()
    {
        var membership = Membership.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        var owner = Role.Create("owner", "Owner", null, RoleScope.Organization, Now).Value;
        var support = Role.Create("support", "Support", null, RoleScope.Global, Now).Value;

        membership.SetRoles([owner], Now).IsSuccess.ShouldBeTrue();
        membership.SetRoles([owner, support], Now).Error.ShouldBe(OrganizationErrors.GlobalRoleNotAssignable);
        membership.Roles.Select(link => link.RoleId).ShouldBe([owner.Id]);
    }
}
