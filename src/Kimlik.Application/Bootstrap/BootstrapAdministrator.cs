using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimlik.Application.Bootstrap;

/// <summary>
/// Gives a new installation its first administrator, from configuration. It acts only while no user or client
/// holds the administrator role, so the settings can stay in place, and it also restores access when the last
/// administrator is gone.
/// </summary>
public sealed partial class BootstrapAdministratorHandler(
    UserManager<User> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog,
    IOptions<BootstrapOptions> options,
    TimeProvider timeProvider,
    ILogger<BootstrapAdministratorHandler> logger)
{
    /// <summary>Runs during database preparation, inside its transaction.</summary>
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (options.Value is not { AdminEmail: { Length: > 0 } email, AdminPassword: { Length: > 0 } password })
        {
            return;
        }

        var adminRoleId = await context.Roles.Where(role => role.Key == SystemRoles.Admin).Select(role => role.Id).SingleAsync(cancellationToken);
        if (await context.UserRoles.AnyAsync(assignment => assignment.RoleId == adminRoleId, cancellationToken)
            || await context.ClientRoles.AnyAsync(assignment => assignment.RoleId == adminRoleId, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = User.Create(email, givenName: null, familyName: null, locale: null, now);
            user.MarkEmailVerified(now);

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{BootstrapOptions.SectionName}: the administrator cannot be created ({string.Join(", ", result.Errors.Select(error => error.Description))}).");
            }

            auditLog.Record(AuditActions.UserCreated, AuditSubject.User(user.Id));
        }
        else if (!user.EmailConfirmed)
        {
            // Anyone could have signed up with the address; only its verified owner may become administrator.
            LogUnverifiedAccount(logger, user.Id);
            return;
        }

        context.UserRoles.Add(new UserRole(user.Id, adminRoleId, now));
        auditLog.Record(AuditActions.UserRolesChanged, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["roles"] = new[] { SystemRoles.Admin } });
        await context.SaveChangesAsync(cancellationToken);

        LogAdministratorBootstrapped(logger, user.Id);
    }

    [LoggerMessage(LogLevel.Warning, "No administrator was bootstrapped: the account {UserId} with the configured address has not verified it")]
    private static partial void LogUnverifiedAccount(ILogger logger, Guid userId);

    [LoggerMessage(LogLevel.Information, "Bootstrapped the administrator {UserId} from configuration")]
    private static partial void LogAdministratorBootstrapped(ILogger logger, Guid userId);
}
