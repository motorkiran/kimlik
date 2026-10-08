using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;

namespace Kimlik.Domain.Tests.Organizations;

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Invitation_CanBeAcceptedOnce_BeforeItExpires()
    {
        var invitation = Create(expiresAt: Now.AddDays(7));
        invitation.IssueToken("hash", Now);

        invitation.Accept(Guid.CreateVersion7(), Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        invitation.TokenHash.ShouldBeNull();
        invitation.Accept(Guid.CreateVersion7(), Now.AddDays(2)).Error.ShouldBe(OrganizationErrors.InvitationClosed);
        invitation.Revoke(Now.AddDays(2)).Error.ShouldBe(OrganizationErrors.InvitationClosed);
    }

    [Fact]
    public void ExpiredInvitation_CannotBeAccepted_UntilRenewed()
    {
        var invitation = Create(expiresAt: Now.AddDays(7));

        invitation.Accept(Guid.CreateVersion7(), Now.AddDays(8)).Error.ShouldBe(OrganizationErrors.InvitationClosed);
        invitation.Renew(Now.AddDays(15), Now.AddDays(8)).IsSuccess.ShouldBeTrue();
        invitation.Accept(Guid.CreateVersion7(), Now.AddDays(9)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Invitation_GrantsOnlyOrganizationRoles()
    {
        var global = Role.Create("support", "Support", null, RoleScope.Global, Now).Value;

        Invitation.Create(Guid.CreateVersion7(), "a@example.com", "A@EXAMPLE.COM", [global], Now.AddDays(7), Now)
            .Error.ShouldBe(OrganizationErrors.GlobalRoleNotAssignable);
    }

    private static Invitation Create(DateTimeOffset expiresAt) =>
        Invitation.Create(Guid.CreateVersion7(), "ada@example.com", "ADA@EXAMPLE.COM", [], expiresAt, Now).Value;
}
