using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Users;

public sealed class ListUserPasskeysHandler(UserPasskeys passkeys)
{
    public Task<Result<IReadOnlyList<PasskeyResponse>>> HandleAsync(Guid userId) => passkeys.ListAsync(userId);
}

/// <summary>Removes a user's passkey, such as one on a device that was stolen.</summary>
public sealed class RemoveUserPasskeyHandler(UserPasskeys passkeys, AccessGuard guard)
{
    public async Task<Result> HandleAsync(Guid userId, string passkeyId, CancellationToken cancellationToken)
    {
        var guardResult = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        return guardResult.IsFailure ? guardResult : await passkeys.RemoveAsync(userId, passkeyId, cancellationToken);
    }
}
