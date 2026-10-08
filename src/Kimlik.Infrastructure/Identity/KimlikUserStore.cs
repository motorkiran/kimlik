using System.Buffers.Text;
using System.Text;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Kimlik.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity's user store with the two-factor secrets protected, which the default store keeps in plain
/// text: authenticator keys are encrypted with the master key and bound to their user, and recovery codes are stored
/// as keyed hashes.
/// </summary>
internal sealed class KimlikUserStore(KimlikDbContext context, ISecretProtector protector, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<User, KimlikDbContext, Guid>(context, describer)
{
    private const string AuthenticatorKeyPurpose = "identity.authenticator-key";
    private const string RecoveryCodePurpose = "identity.recovery-code";

    public override Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken)
    {
        var encrypted = protector.Protect(Encoding.UTF8.GetBytes(key), AuthenticatorKeyPurpose, user.Id.ToByteArray());
        return base.SetAuthenticatorKeyAsync(user, Convert.ToBase64String(encrypted), cancellationToken);
    }

    public override async Task<string?> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken) =>
        await base.GetAuthenticatorKeyAsync(user, cancellationToken) is { Length: > 0 } stored
            ? Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(stored), AuthenticatorKeyPurpose, user.Id.ToByteArray()))
            : null;

    public override Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        base.ReplaceCodesAsync(user, recoveryCodes.Select(HashRecoveryCode), cancellationToken);

    public override Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken) =>
        base.RedeemCodeAsync(user, HashRecoveryCode(code), cancellationToken);

    /// <summary>Codes are compared as uppercase without spaces, so they can be typed the way they are read.</summary>
    private string HashRecoveryCode(string code) =>
        Base64Url.EncodeToString(protector.Hash(Encoding.UTF8.GetBytes(code.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()), RecoveryCodePurpose));
}
