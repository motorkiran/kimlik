using System.Net;
using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;

namespace Kimlik.Server.Tests.Organizations;

public sealed class InvitationTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InvitedUser_JoinsThroughTheEmailedLink_WithTheRoles()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();
        var invitee = await server.CreateUserAsync();

        using var invited = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = invitee.Email, Roles = [organization.Role] });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await invited.ReadAsync<InvitationResponse>()).Status.ShouldBe(InvitationStatus.Pending);

        var link = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(invitee.Email, "invited to join"));
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(invitee.Email, invitee.Password);
        var page = await browser.GetPageAsync(link);
        page.Text.ShouldContain("You have been invited to join Acme.");

        var joined = await Browser.ReadPageAsync(await browser.SubmitAsync(page));
        joined.Text.ShouldContain("Welcome to Acme");

        using var members = await api.Http.GetAsync($"/api/v1/organizations/{organization.Id}/members", CancellationToken);
        var member = (await members.ReadAsync<Page<MemberResponse>>()).Items.ShouldHaveSingleItem();
        member.UserId.ShouldBe(invitee.Id);
        member.Roles.ShouldBe([organization.Role]);

        (await browser.GetPageAsync(link)).Text.ShouldContain("This invitation is no longer valid.");
    }

    [Fact]
    public async Task SomeoneElse_CannotAcceptTheInvitation()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var stranger = await server.CreateUserAsync();
        using var invited = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = email });

        var link = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(email, "invited to join"));
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(stranger.Email, stranger.Password);
        var page = await browser.GetPageAsync(link);

        page.Text.ShouldContain($"The invitation was sent to {email}, but you are signed in as {stranger.Email}.");
        page.Document.QuerySelector("form[method=post]").ShouldBeNull();
    }

    [Fact]
    public async Task SignedOutVisitor_IsAskedToSignInOrSignUp()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();
        var email = $"newcomer-{Guid.NewGuid():N}@example.com";
        using var invited = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = email });

        var link = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(email, "invited to join"));
        using var browser = new Browser(server);
        var page = await browser.GetPageAsync(link);

        page.Text.ShouldContain($"Sign in, or create an account, with {email} to accept.");
        page.Document.QuerySelector("a[href^='/signup']")!.GetAttribute("href")!.ShouldContain(Uri.EscapeDataString("/invitations/accept"));
    }

    [Fact]
    public async Task ResendingOrRevoking_InvalidatesEarlierLinks()
    {
        using var api = await server.CreateApiClientAsync();
        var organization = await server.CreateOrganizationAsync();
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        using var invited = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = email });
        var invitation = await invited.ReadAsync<InvitationResponse>();
        var first = CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(email, "invited to join"));

        using var resent = await api.Http.PostAsync($"{Invitations(organization)}/{invitation.Id}/resend");
        resent.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = CapturingEmailSender.LinkIn(await WaitForEmailAsync(email, count: 2));

        using var browser = new Browser(server);
        (await browser.GetPageAsync(first)).Text.ShouldContain("This invitation is no longer valid.");
        (await browser.GetPageAsync(second)).Text.ShouldContain("You have been invited to join Acme.");

        using var revoked = await api.Http.DeleteAsync($"{Invitations(organization)}/{invitation.Id}", CancellationToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await browser.GetPageAsync(second)).Text.ShouldContain("This invitation is no longer valid.");

        using var listed = await api.Http.GetAsync(Invitations(organization), CancellationToken);
        (await listed.ReadAsync<Page<InvitationResponse>>()).Items.ShouldHaveSingleItem().Status.ShouldBe(InvitationStatus.Revoked);
    }

    [Fact]
    public async Task Members_AndPendingInvitees_CannotBeInvitedAgain()
    {
        using var api = await server.CreateApiClientAsync();
        var member = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(member.Id);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        using var first = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = email });

        using var memberAgain = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = member.Email.ToUpperInvariant() });
        using var inviteeAgain = await api.Http.PostJsonAsync(Invitations(organization), new CreateInvitationRequest { Email = email });

        (await memberAgain.ReadProblemCodeAsync()).ShouldBe("organization.already_member");
        (await inviteeAgain.ReadProblemCodeAsync()).ShouldBe("invitation.pending");
    }

    private static string Invitations(TestOrganization organization) => $"/api/v1/organizations/{organization.Id}/invitations";

    private async Task<EmailMessage> WaitForEmailAsync(string address, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (server.Emails.SentTo(address).Count() < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }

        return server.Emails.SentTo(address).Last();
    }
}
