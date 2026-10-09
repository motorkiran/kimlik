using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Application.ApiKeys;
using Kimlik.Application.Auditing;
using Kimlik.Application.Common;
using Kimlik.Application.Organizations;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Users;

/// <summary>
/// Gathers what Kimlik holds about a user into one export, for the user on the account pages or for an administrator
/// answering a request. Every export is audited.
/// </summary>
public sealed class PersonalDataExporter(
    UserManager<User> userManager,
    IKimlikDbContext context,
    MyOrganizations organizations,
    ExternalLogins logins,
    UserPasskeys passkeys,
    ApplicationSessions sessions,
    ApiKeyStore apiKeys,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>The most audit events an export carries; retention keeps fewer than this for almost everyone.</summary>
    public const int MaxActivity = 10_000;

    public async Task<Result<PersonalDataExport>> ExportAsync(Guid userId, bool withPrivateMetadata, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var id = userId.ToString();
        var keys = await context.ApiKeys.AsNoTracking().Where(key => key.UserId == userId).OrderBy(key => key.Id).ToListAsync(cancellationToken);
        var keyPermissions = await apiKeys.PermissionsOfAsync([.. keys.Select(key => key.Id)], cancellationToken);
        var subscriptions = await context.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderBy(subscription => subscription.CreatedAt)
            .ToListAsync(cancellationToken);
        var activity = await context.AuditEvents.AsNoTracking()
            .Where(auditEvent => (auditEvent.SubjectType == "user" && auditEvent.SubjectId == id)
                || (auditEvent.ActorType == AuditActorType.User && auditEvent.ActorId == id))
            .OrderByDescending(auditEvent => auditEvent.Id)
            .Take(MaxActivity)
            .ToListAsync(cancellationToken);

        var export = new PersonalDataExport(
            timeProvider.GetUtcNow(),
            await context.ToProfileAsync(user, cancellationToken),
            withPrivateMetadata ? Metadata.Parse(user.PrivateMetadata) : null,
            await organizations.ListAsync(userId, cancellationToken),
            (await logins.ListAsync(userId)).Value,
            (await passkeys.ListAsync(userId)).Value,
            (await sessions.ListAsync(userId, cancellationToken)).Value,
            [.. keys.Select(key => key.ToResponse(keyPermissions[key.Id]))],
            await context.ToResponsesAsync(subscriptions, cancellationToken),
            [.. activity.Select(ListAuditEventsHandler.ToResponse)]);

        auditLog.Record(AuditActions.UserDataExported, AuditSubject.User(userId));
        await context.SaveChangesAsync(cancellationToken);
        return export;
    }
}

/// <summary>An administrator's export of a user's data, with the private metadata.</summary>
public sealed class ExportUserDataHandler(PersonalDataExporter exporter)
{
    public Task<Result<PersonalDataExport>> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        exporter.ExportAsync(userId, withPrivateMetadata: true, cancellationToken);
}
