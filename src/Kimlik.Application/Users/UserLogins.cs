using Kimlik.Application.Access;
using Kimlik.Application.Accounts;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Users;

public sealed class ListUserLoginsHandler(ExternalLogins logins)
{
    public Task<Result<IReadOnlyList<UserLoginResponse>>> HandleAsync(Guid userId) => logins.ListAsync(userId);
}

/// <summary>
/// Disconnects a user's account at another provider, such as one that was compromised. Unlike self-service, it may be
/// the user's last way to sign in; they can still set a password through a reset.
/// </summary>
public sealed class UnlinkUserLoginHandler(ExternalLogins logins, AccessGuard guard)
{
    public async Task<Result> HandleAsync(Guid userId, string provider, CancellationToken cancellationToken)
    {
        var guardResult = await guard.EnsureCanManageUserAsync(userId, cancellationToken);
        return guardResult.IsFailure ? guardResult : await logins.UnlinkAsync(userId, provider, keepASignInMethod: false, cancellationToken);
    }
}
