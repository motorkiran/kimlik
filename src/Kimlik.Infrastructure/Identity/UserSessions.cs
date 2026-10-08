using Kimlik.Application.Abstractions;
using OpenIddict.Abstractions;

namespace Kimlik.Infrastructure.Identity;

internal sealed class UserSessions(IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens) : IUserSessions
{
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();

        // Revoking the authorizations covers tokens attached to them; ad-hoc tokens are revoked directly.
        await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);
    }
}
