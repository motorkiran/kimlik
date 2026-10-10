using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Oidc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.Accounts;

/// <summary>Phone numbers on accounts, verified with a texted code, and signing in with codes texted to them.</summary>
public sealed class PhoneNumberTests(KimlikServerFixture server)
{
    [Fact]
    public async Task VerifiedNumber_SignsIn_AndIsInTokens()
    {
        var user = await server.CreateUserAsync();
        var (written, number) = NewNumber();
        using (var browser = new Browser(server))
        {
            using var signIn = await browser.SignInAsync(user.Email, user.Password);
            await AddPhoneNumberAsync(browser, written, number);
        }

        var client = await CreateClientAsync("openid phone");
        var request = new AuthorizationRequest(client.ClientId) { Scope = "openid phone" };
        using var phoneBrowser = new Browser(server);
        var page = await phoneBrowser.GetPageAsync($"/signin/phone?returnUrl={Uri.EscapeDataString(request.Url)}");
        using var asked = await phoneBrowser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = written });
        var codePage = await phoneBrowser.GetPageAsync(asked.Headers.Location!.OriginalString);
        codePage.Text.ShouldContain($"If an account has the number {number}, we texted it a code.");
        using var signedIn = await phoneBrowser.SubmitAsync(
            codePage, new Dictionary<string, string> { ["Input.Code"] = await server.Texts.WaitForCodeAsync(number, count: 2) }, "section.card form");
        signedIn.Headers.Location!.OriginalString.ShouldBe(request.Url);
        using var callback = await phoneBrowser.FollowAsync(signedIn);
        var tokens = await OidcFlows.RedeemCodeAsync(phoneBrowser.Client, client, request, AuthorizationRequest.ReadCallback(callback)["code"]);

        var identity = Payload(tokens.GetProperty("id_token").GetString()!);
        identity.GetProperty("amr").EnumerateArray().Select(method => method.GetString()).ShouldBe(["sms"]);
        identity.GetProperty("phone_number").GetString().ShouldBe(number);
        identity.GetProperty("phone_number_verified").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task NumberOnAnotherAccount_IsRefused_OnlyOnceItsCodeIsRight()
    {
        var (written, number) = NewNumber();
        var owner = await server.CreateUserAsync();
        using (var browser = new Browser(server))
        {
            using var signIn = await browser.SignInAsync(owner.Email, owner.Password);
            await AddPhoneNumberAsync(browser, written, number);
        }

        var other = await server.CreateUserAsync();
        using var otherBrowser = new Browser(server);
        using var otherSignIn = await otherBrowser.SignInAsync(other.Email, other.Password);
        var page = await otherBrowser.GetPageAsync("/account/phone");
        var sent = await Browser.ReadPageAsync(await otherBrowser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = number }, "#add-phone"));
        using var confirmed = await otherBrowser.SubmitAsync(
            sent, new Dictionary<string, string> { ["Input.Code"] = await server.Texts.WaitForCodeAsync(number, count: 2) }, "#confirm-phone");

        (await Browser.ReadPageAsync(confirmed)).Text.ShouldContain("That number is on another account.");
        (await server.QueryDatabaseAsync(context => context.Users.Where(user => user.Id == other.Id).Select(user => user.PhoneNumber).SingleAsync())).ShouldBeNull();
    }

    [Fact]
    public async Task UnknownNumber_LooksTheSame_AndGetsNoText()
    {
        var (written, number) = NewNumber();
        using var browser = new Browser(server);

        var page = await browser.GetPageAsync("/signin/phone");
        using var asked = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = written });
        var codePage = await browser.GetPageAsync(asked.Headers.Location!.OriginalString);
        using var wrong = await browser.SubmitAsync(codePage, new Dictionary<string, string> { ["Input.Code"] = "123456" }, "section.card form");

        codePage.Text.ShouldContain($"If an account has the number {number}, we texted it a code.");
        (await Browser.ReadPageAsync(wrong)).Text.ShouldContain("That code is not right, or it has expired.");
        server.Texts.SentTo(number).ShouldBeEmpty();
    }

    [Fact]
    public async Task Administrator_RemovesANumber_AndTextsStop()
    {
        var user = await server.CreateUserAsync();
        var (written, number) = NewNumber();
        using (var browser = new Browser(server))
        {
            using var signIn = await browser.SignInAsync(user.Email, user.Password);
            await AddPhoneNumberAsync(browser, written, number);
        }

        using var api = await server.CreateApiClientAsync();
        using var shown = await api.Http.GetAsync($"/api/v1/users/{user.Id}", TestContext.Current.CancellationToken);
        (await shown.ReadAsync<UserResponse>()).PhoneNumber.ShouldBe(number);
        using var removed = await api.Http.DeleteAsync($"/api/v1/users/{user.Id}/phone-number", TestContext.Current.CancellationToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var phoneBrowser = new Browser(server);
        var page = await phoneBrowser.GetPageAsync("/signin/phone");
        using var asked = await phoneBrowser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = number });
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        server.Texts.SentTo(number).Count().ShouldBe(1);
        (await server.QueryDatabaseAsync(context => context.AuditEvents.AnyAsync(auditEvent =>
            auditEvent.SubjectId == user.Id.ToString() && auditEvent.Action == AuditActions.UserPhoneNumberRemoved))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("+90 532 123 45 67", null, "+905321234567")]
    [InlineData("0090 (532) 123-45-67", null, "+905321234567")]
    [InlineData("0532 123 45 67", "90", "+905321234567")]
    [InlineData("532 123 45 67", "90", "+905321234567")]
    [InlineData("0532 123 45 67", null, null)]
    [InlineData("+90 532 ABC 45 67", "90", null)]
    [InlineData("+0532", null, null)]
    [InlineData("+1234567", null, null)]
    public void PhoneNumbers_AreReadAsPeopleWriteThem(string written, string? defaultCountryCode, string? expected)
    {
        PhoneNumbers.Normalize(written, defaultCountryCode).ShouldBe(expected);
    }

    [Fact]
    public void Texts_GoOnlyToTheAllowedCountries()
    {
        var turkeyOnly = new SmsOptions { AllowedCountryCodes = ["90"] };

        PhoneNumbers.IsAllowed("+905321234567", turkeyOnly).ShouldBeTrue();
        PhoneNumbers.IsAllowed("+12025550123", turkeyOnly).ShouldBeFalse();
        PhoneNumbers.IsAllowed("+12025550123", new SmsOptions()).ShouldBeTrue();
    }

    /// <summary>Adds the number on the account pages, with the code texted to it, in a browser that signed in just now.</summary>
    private async Task AddPhoneNumberAsync(Browser browser, string written, string number)
    {
        var page = await browser.GetPageAsync("/account/phone");
        var sent = await Browser.ReadPageAsync(await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.PhoneNumber"] = written }, "#add-phone"));
        sent.Text.ShouldContain($"We texted a code to {number}.");
        using var confirmed = await browser.SubmitAsync(sent, new Dictionary<string, string> { ["Input.Code"] = await server.Texts.WaitForCodeAsync(number) }, "#confirm-phone");
        confirmed.Headers.Location!.OriginalString.ShouldBe("/account/phone?saved=true");
    }

    private async Task<TestWebClient> CreateClientAsync(string scope)
    {
        using var api = await server.CreateApiClientAsync();
        var clientId = $"phone-{Guid.NewGuid():N}"[..24];
        using var created = await api.Http.PostJsonAsync("/api/v1/clients", new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = "Orders web app",
            Type = ClientType.Spa,
            RedirectUris = [TestWebClient.RedirectUri],
            Scopes = scope.Split(' '),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new TestWebClient(clientId);
    }

    /// <summary>A Turkish mobile number nobody else in the test run has, as a person writes it and in E.164.</summary>
    private static (string Written, string Number) NewNumber()
    {
        var subscriber = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ($"0{subscriber[..3]} {subscriber[3..6]} {subscriber[6..]}", $"+90{subscriber}");
    }

    private static JsonElement Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]))).RootElement.Clone();
}
