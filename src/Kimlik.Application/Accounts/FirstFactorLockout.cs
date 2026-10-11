using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

/// <summary>The lockout after a right first factor, kept as ASP.NET Core Identity keeps it after a right password.</summary>
public static class FirstFactorLockout
{
    /// <summary>
    /// Forgets the wrong guesses, unless a second factor follows: wrong second-factor codes count toward the same lockout,
    /// and whoever has the first factor could otherwise wipe them between guesses.
    /// </summary>
    public static async Task FirstFactorSucceededAsync(this UserManager<User> userManager, User user)
    {
        if (!user.TwoFactorEnabled)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }
    }
}
