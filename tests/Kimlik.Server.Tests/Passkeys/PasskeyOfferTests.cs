using Kimlik.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>The offer to add a passkey, once, after a password sign-in.</summary>
public sealed class PasskeyOfferTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PasswordSignIn_OffersAPasskey_Once()
    {
        var user = await server.CreateUserAsync(passkeyOffered: false);

        using (var first = new Browser(server))
        {
            using var signIn = await first.SignInAsync(user.Email, user.Password);
            signIn.Headers.Location!.OriginalString.ShouldBe("/signin/passkey?returnUrl=%2F");
            (await first.GetPageAsync(signIn.Headers.Location.OriginalString)).Text.ShouldContain("Sign in faster with a passkey");
        }

        using var second = new Browser(server);
        using var again = await second.SignInAsync(user.Email, user.Password);

        again.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task PasskeyAddedFromTheOffer_GoesOnToWhereTheSignInWasGoing()
    {
        var user = await server.CreateUserAsync(passkeyOffered: false);
        using var authenticator = new SoftwareAuthenticator();
        using var browser = new Browser(server);
        using var signIn = await browser.SignInAsync(user.Email, user.Password, returnUrl: "/account/sessions");
        var offer = await browser.GetPageAsync(signIn.Headers.Location!.OriginalString);

        using var added = await browser.RunPasskeyFormAsync(offer, "#add-passkey", options => authenticator.Create(options));

        added.Headers.Location!.OriginalString.ShouldBe("/account/sessions");
        (await server.QueryDatabaseAsync(context => context.UserPasskeys.CountAsync(passkey => passkey.UserId == user.Id, CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task PeopleWithAPasskey_AreNotOffered()
    {
        var user = await server.CreateUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        using (var browser = new Browser(server))
        {
            using var signIn = await browser.SignInAsync(user.Email, user.Password);
            await browser.AddPasskeyAsync(authenticator);
        }

        await server.QueryDatabaseAsync(context => context.Users.Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.PasskeyOfferedAt, (DateTimeOffset?)null), CancellationToken));
        using var again = new Browser(server);
        using var signedIn = await again.SignInAsync(user.Email, user.Password);

        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
    }
}
