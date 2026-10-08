using System.Net;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Mfa;

/// <summary>Two-factor authentication through the Account API, with codes computed like an authenticator app.</summary>
public sealed class MfaApiTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_SetsUpAnAuthenticator_ThenReplacesCodes_AndTurnsItOff()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        using var started = await me.PostAsync("/api/v1/me/mfa/authenticator");
        var setup = await started.ReadAsync<AuthenticatorSetupResponse>();
        setup.OtpAuthUri.ShouldStartWith("otpauth://totp/Kimlik:");
        setup.OtpAuthUri.ShouldContain(Uri.EscapeDataString(user.Email));
        var codes = await TotpCodes.NextThreeAsync(setup.Secret);

        using var wrong = await me.PostJsonAsync("/api/v1/me/mfa/authenticator/confirm", new AuthenticatorCodeRequest { Code = "000000" });
        (await wrong.ReadProblemCodeAsync()).ShouldBe("mfa.invalid_code");

        using var confirmed = await me.PostJsonAsync("/api/v1/me/mfa/authenticator/confirm", new AuthenticatorCodeRequest { Code = codes[0] });
        var recovery = await confirmed.ReadAsync<RecoveryCodesResponse>();
        recovery.Codes.Count.ShouldBe(10);
        (await StatusAsync(me)).ShouldBe(new MfaStatusResponse(Enabled: true, Required: false, RecoveryCodesLeft: 10));

        using var again = await me.PostAsync("/api/v1/me/mfa/authenticator");
        (await again.ReadProblemCodeAsync()).ShouldBe("mfa.already_enabled");

        using var replaced = await me.PostJsonAsync("/api/v1/me/mfa/recovery-codes", new AuthenticatorCodeRequest { Code = codes[1] });
        (await replaced.ReadAsync<RecoveryCodesResponse>()).Codes.ShouldNotBe(recovery.Codes);

        using var reused = await me.PostJsonAsync("/api/v1/me/mfa/disable", new AuthenticatorCodeRequest { Code = codes[1] });
        (await reused.ReadProblemCodeAsync()).ShouldBe("mfa.invalid_code");

        using var disabled = await me.PostJsonAsync("/api/v1/me/mfa/disable", new AuthenticatorCodeRequest { Code = codes[2] });
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(me)).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task WrongCodes_LockTheAccount_AsAtSignIn()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        var factor = await server.EnableMfaAsync(user.Id);
        var wrong = new AuthenticatorCodeRequest { Code = "000000" };

        for (var attempt = 1; attempt < 5; attempt++)
        {
            using var refused = await me.PostJsonAsync("/api/v1/me/mfa/disable", wrong);
            (await refused.ReadProblemCodeAsync()).ShouldBe("mfa.invalid_code");
        }

        using var fifth = await me.PostJsonAsync("/api/v1/me/mfa/recovery-codes", wrong);
        (await fifth.ReadProblemCodeAsync()).ShouldBe("account.locked_out");

        var codes = await TotpCodes.NextThreeAsync(factor.Secret);
        using var right = await me.PostJsonAsync("/api/v1/me/mfa/disable", new AuthenticatorCodeRequest { Code = codes[1] });
        (await right.ReadProblemCodeAsync()).ShouldBe("account.locked_out");
    }

    [Fact]
    public async Task Administrators_CannotTurnItOff_WhenThePolicyRequiresIt()
    {
        await using var kimlik = await StartWithAdministratorPolicyAsync();
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);

        // An API client signs in through the shared host, then calls the host with the policy on.
        var token = await server.UserAccessTokenAsync(admin);
        using var me = kimlik.CreateClient();
        me.DefaultRequestHeaders.Authorization = new("Bearer", token);

        (await StatusAsync(me)).Required.ShouldBeTrue();
        using var started = await me.PostAsync("/api/v1/me/mfa/authenticator");
        var codes = await TotpCodes.NextThreeAsync((await started.ReadAsync<AuthenticatorSetupResponse>()).Secret);
        using var confirmed = await me.PostJsonAsync("/api/v1/me/mfa/authenticator/confirm", new AuthenticatorCodeRequest { Code = codes[0] });

        using var refused = await me.PostJsonAsync("/api/v1/me/mfa/disable", new AuthenticatorCodeRequest { Code = codes[1] });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemCodeAsync()).ShouldBe("mfa.required");
    }

    [Fact]
    public async Task Administrator_ResetsAUsersSecondFactor()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        using var started = await me.PostAsync("/api/v1/me/mfa/authenticator");
        var codes = await TotpCodes.NextThreeAsync((await started.ReadAsync<AuthenticatorSetupResponse>()).Secret);
        using var confirmed = await me.PostJsonAsync("/api/v1/me/mfa/authenticator/confirm", new AuthenticatorCodeRequest { Code = codes[0] });
        using var api = await server.CreateApiClientAsync();

        using var reset = await api.Http.PostAsync($"/api/v1/users/{user.Id}/mfa/reset");

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var audit = await api.Http.GetAsync($"/api/v1/audit-events?subjectType=user&subjectId={user.Id}&action=user.mfa_reset", CancellationToken);
        (await audit.ReadAsync<Contracts.Management.Page<Contracts.Management.AuditEventResponse>>()).Items.ShouldHaveSingleItem();
    }

    private static async Task<MfaStatusResponse> StatusAsync(HttpClient me)
    {
        using var response = await me.GetAsync("/api/v1/me/mfa", CancellationToken);
        return await response.ReadAsync<MfaStatusResponse>();
    }

    private async Task<WebApplicationFactory<Program>> StartWithAdministratorPolicyAsync()
    {
        var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kimlik:Mfa:RequireForAdministrators"] = "true" })));
        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        return kimlik;
    }
}
