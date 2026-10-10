using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using OpenIddict.Abstractions;

namespace Kimlik.Infrastructure.Identity;

internal sealed class UserSessions(IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, BackChannelLogout backChannelLogout)
    : IUserSessions
{
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();

        // Revoking the authorizations covers tokens attached to them; ad-hoc tokens are revoked directly.
        await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);

        // The web apps the user signed in to hear of it, once the caller saves.
        await backChannelLogout.EndAllAsync(userId, cancellationToken);
    }
}
