using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;

namespace Kimlik.Application.Provisioning;

/// <summary>
/// Applies a provisioning document in one transaction: all of it or nothing. Each change is audited like the
/// same change through the API, and the caller's own access limits what it may grant.
/// </summary>
public sealed class ApplyProvisioningHandler(
    IKimlikDbContext context,
    IAuditLog auditLog,
    PermissionProvisioner permissions,
    RoleProvisioner roles,
    ApiResourceProvisioner apiResources,
    ClientProvisioner clients)
{
    public async Task<Result<ProvisioningResult>> HandleAsync(ProvisioningDocument document, CancellationToken cancellationToken)
    {
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var changes = new List<ProvisioningChange>();

        // In dependency order: roles name permissions, and clients name API resources and roles.
        var error = await ApplyAsync("permissions", document.Permissions, item => item.Key, permissions.ApplyAsync, changes, cancellationToken)
            ?? await ApplyAsync("roles", document.Roles, item => item.Key, roles.ApplyAsync, changes, cancellationToken)
            ?? await ApplyAsync("apiResources", document.ApiResources, item => item.Scope, apiResources.ApplyAsync, changes, cancellationToken)
            ?? await ApplyAsync("clients", document.Clients, item => item.ClientId, clients.ApplyAsync, changes, cancellationToken);

        if (error is not null)
        {
            return error;
        }

        var result = new ProvisioningResult(
            changes.Count(change => change == ProvisioningChange.Created),
            changes.Count(change => change == ProvisioningChange.Updated),
            changes.Count(change => change == ProvisioningChange.Unchanged));

        if (result.Created + result.Updated > 0)
        {
            auditLog.Record(AuditActions.ProvisioningApplied, data: new Dictionary<string, object?>
            {
                ["created"] = result.Created,
                ["updated"] = result.Updated,
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<Error?> ApplyAsync<T>(
        string list,
        IReadOnlyList<T>? items,
        Func<T, string> keyOf,
        Func<T, CancellationToken, Task<Result<ProvisioningChange>>> apply,
        List<ProvisioningChange> changes,
        CancellationToken cancellationToken)
    {
        foreach (var (index, item) in (items ?? []).Index())
        {
            var applied = await apply(item, cancellationToken);
            if (applied.IsFailure)
            {
                return ProvisioningErrors.At(list, index, keyOf(item), applied.Error);
            }

            changes.Add(applied.Value);
        }

        return null;
    }
}
