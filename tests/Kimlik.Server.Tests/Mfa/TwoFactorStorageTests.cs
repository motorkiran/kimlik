using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Identity;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Server.Tests.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Mfa;

public sealed class TwoFactorStorageTests(KimlikServerFixture server)
{
    [Fact]
    public async Task AuthenticatorKey_IsEncryptedAtRest()
    {
        var account = await server.CreateUserAsync();

        await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(account.Id.ToString()))!;
            (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.ShouldBeTrue();

            var key = await users.GetAuthenticatorKeyAsync(user);
            Base32.Decode(key!)!.Length.ShouldBe(20);
            (await StoredTokensAsync(services, account.Id)).ShouldNotContain(key);
        });
    }

    [Fact]
    public async Task RecoveryCodes_AreStoredHashed_AndWorkOnce()
    {
        var account = await server.CreateUserAsync();

        await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(account.Id.ToString()))!;
            var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();

            var stored = await StoredTokensAsync(services, account.Id);
            codes.ShouldAllBe(code => !stored.Any(value => value!.Contains(code, StringComparison.OrdinalIgnoreCase)));

            (await users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0].ToLowerInvariant())).Succeeded.ShouldBeTrue();
            (await users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded.ShouldBeFalse();
            (await users.CountRecoveryCodesAsync(user)).ShouldBe(9);
        });
    }

    [Fact]
    public async Task AuthenticatorCode_IsAcceptedOnce()
    {
        var account = await server.CreateUserAsync();

        await server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(account.Id.ToString()))!;
            await users.ResetAuthenticatorKeyAsync(user);
            var secret = Base32.Decode((await users.GetAuthenticatorKeyAsync(user))!)!;
            var step = Totp.StepAt(DateTimeOffset.UtcNow);

            // A code from the previous step still works once, but not after a newer one was used.
            var previous = Totp.Compute(secret, step - 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var current = Totp.Compute(secret, step).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var tooOld = Totp.Compute(secret, step - 2).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

            (await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, tooOld)).ShouldBeFalse();
            (await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, current)).ShouldBeTrue();
            (await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, current)).ShouldBeFalse();
            (await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, previous)).ShouldBeFalse();
        });
    }

    private static Task<List<string?>> StoredTokensAsync(IServiceProvider services, Guid userId) =>
        services.GetRequiredService<KimlikDbContext>().UserTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .Select(token => token.Value)
            .ToListAsync(TestContext.Current.CancellationToken);
}
