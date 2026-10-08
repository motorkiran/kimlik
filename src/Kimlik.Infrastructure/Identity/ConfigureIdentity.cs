using Kimlik.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Identity;

/// <summary>Applies Kimlik's account policies to ASP.NET Core Identity.</summary>
internal sealed class ConfigureIdentity(IOptions<AccountOptions> accounts)
    : IConfigureOptions<IdentityOptions>, IConfigureOptions<PasswordHasherOptions>, IConfigureOptions<DataProtectionTokenProviderOptions>
{
    // OWASP's current recommendation for PBKDF2-HMAC-SHA512.
    private const int PasswordHashIterations = 210_000;

    public void Configure(IdentityOptions options)
    {
        var policy = accounts.Value;

        // Version 3 adds passkeys to the store.
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = policy.RequireVerifiedEmail;

        // Length only; composition rules make passwords harder to remember, not stronger.
        options.Password.RequiredLength = policy.PasswordMinimumLength;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = policy.MaxFailedSignInAttempts;
        options.Lockout.DefaultLockoutTimeSpan = policy.LockoutDuration;
    }

    public void Configure(PasswordHasherOptions options) => options.IterationCount = PasswordHashIterations;

    /// <summary>Email confirmation links; password reset links have their own, shorter lifetime.</summary>
    public void Configure(DataProtectionTokenProviderOptions options) => options.TokenLifespan = TimeSpan.FromHours(48);
}
