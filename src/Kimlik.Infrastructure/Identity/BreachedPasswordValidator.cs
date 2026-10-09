using System.Security.Cryptography;
using System.Text;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Identity;

/// <summary>
/// Refuses new passwords that appear in known data breaches (NIST SP 800-63B), through Have I Been Pwned's Pwned
/// Passwords range API: only the first five characters of the password's SHA-1 hash leave Kimlik, and the answer is
/// padded so its size tells nothing either. If the service cannot be reached, the password is accepted, so that an
/// outage blocks nobody.
/// </summary>
internal sealed partial class BreachedPasswordValidator(
    IHttpClientFactory httpClients, IOptions<AccountOptions> options, ILogger<BreachedPasswordValidator> logger) : IPasswordValidator<User>
{
    public const string HttpClientName = "Kimlik.PwnedPasswords";

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        if (!options.Value.BreachedPasswordCheck || string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

#pragma warning disable CA5350 // The range API is keyed by SHA-1; the hash only finds the range and protects nothing.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        string range;
        try
        {
            range = await httpClients.CreateClient(HttpClientName).GetStringAsync($"range/{hash[..5]}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogUnavailable(logger, exception);
            return IdentityResult.Success;
        }

        // Each line is the rest of a hash and how often it was seen; padding lines have a count of zero.
        var suffix = hash.AsSpan(5);
        foreach (var line in range.AsSpan().EnumerateLines())
        {
            if (line.Length > suffix.Length && line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase) && line[suffix.Length] == ':'
                && !line[(suffix.Length + 1)..].Trim().SequenceEqual("0"))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = AccountErrors.PasswordBreachedCode,
                    Description = "The password has appeared in a data breach.",
                });
            }
        }

        return IdentityResult.Success;
    }

    [LoggerMessage(LogLevel.Warning, "Pwned Passwords could not be reached; the password was accepted unchecked")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
