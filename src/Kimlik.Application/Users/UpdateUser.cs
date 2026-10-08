using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Users;

public sealed class UpdateUserHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<UserResponse>> HandleAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var guardResult = await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (guardResult.IsFailure)
        {
            return guardResult.Error;
        }

        var publicMetadata = request.HasPublicMetadata ? Metadata.Serialize(request.PublicMetadata) : user.PublicMetadata;
        var privateMetadata = request.HasPrivateMetadata ? Metadata.Serialize(request.PrivateMetadata) : user.PrivateMetadata;
        if (publicMetadata.IsFailure || privateMetadata.IsFailure)
        {
            return Metadata.TooLarge;
        }

        var pictureUrl = request.HasPictureUrl ? request.PictureUrl : user.PictureUrl;
        var timeZone = request.HasTimeZone ? request.TimeZone : user.TimeZone;
        if (ProfileFields.Check(pictureUrl, timeZone) is { } invalid)
        {
            return invalid;
        }

        var now = timeProvider.GetUtcNow();
        user.UpdateProfile(
            request.HasGivenName ? request.GivenName : user.GivenName,
            request.HasFamilyName ? request.FamilyName : user.FamilyName,
            request.HasLocale ? request.Locale : user.Locale,
            pictureUrl,
            timeZone,
            now);
        user.SetMetadata(publicMetadata.Value, privateMetadata.Value, now);

        auditLog.Record(AuditActions.UserUpdated, AuditSubject.User(user.Id));
        await context.SaveChangesAsync(cancellationToken);

        return await context.ToResponseAsync(user, cancellationToken);
    }
}
