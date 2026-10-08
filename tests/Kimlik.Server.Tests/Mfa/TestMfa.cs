using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Mfa;

internal sealed record TestSecondFactor(string Secret, IReadOnlyList<string> RecoveryCodes);

internal static class TestMfa
{
    /// <summary>Turns two-factor authentication on for the user directly, as if they had set it up.</summary>
    public static Task<TestSecondFactor> EnableMfaAsync(this KimlikServerFixture server, Guid userId) =>
        server.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            return new TestSecondFactor((await users.GetAuthenticatorKeyAsync(user))!, [.. codes!]);
        });
}
