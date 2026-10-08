using Kimlik.Application.Abstractions;
using Kimlik.Application.Plans;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Provisioning;

public sealed class FeatureProvisioner(IKimlikDbContext context, CreateFeatureHandler create, UpdateFeatureHandler update)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedFeature declared, CancellationToken cancellationToken)
    {
        var existing = await context.Features.AsNoTracking().SingleOrDefaultAsync(feature => feature.Key == declared.Key, cancellationToken);
        if (existing is null)
        {
            var created = await create.HandleAsync(
                new CreateFeatureRequest { Key = declared.Key, Name = declared.Name, Description = declared.Description, Type = declared.Type },
                cancellationToken);
            return created.IsSuccess ? ProvisioningChange.Created : created.Error;
        }

        if (existing.Type.ToString() != declared.Type.ToString())
        {
            return ProvisioningErrors.FixedProperty("type");
        }

        if (existing.Name == declared.Name.Trim() && existing.Description == Declared.Text(declared.Description))
        {
            return ProvisioningChange.Unchanged;
        }

        var updated = await update.HandleAsync(existing.Id, new UpdateFeatureRequest { Name = declared.Name, Description = declared.Description }, cancellationToken);
        return updated.IsSuccess ? ProvisioningChange.Updated : updated.Error;
    }
}

public sealed class PlanProvisioner(IKimlikDbContext context, CreatePlanHandler create, UpdatePlanHandler update)
{
    public async Task<Result<ProvisioningChange>> ApplyAsync(ProvisionedPlan declared, CancellationToken cancellationToken)
    {
        var existing = await context.Plans.AsNoTracking().Include(plan => plan.Features).SingleOrDefaultAsync(plan => plan.Key == declared.Key, cancellationToken);
        if (existing is null)
        {
            var created = await create.HandleAsync(
                new CreatePlanRequest
                {
                    Key = declared.Key,
                    Name = declared.Name,
                    Description = declared.Description,
                    Features = declared.Features ?? new Dictionary<string, System.Text.Json.JsonElement>(),
                },
                cancellationToken);
            if (created.IsFailure)
            {
                return created.Error;
            }

            if (declared.IsArchived)
            {
                var archived = await update.HandleAsync(created.Value.Id, new UpdatePlanRequest { IsArchived = true }, cancellationToken);
                if (archived.IsFailure)
                {
                    return archived.Error;
                }
            }

            return ProvisioningChange.Created;
        }

        var features = await context.FeaturesByKeyAsync(cancellationToken);
        var featuresChanged = false;
        if (declared.Features is not null)
        {
            var settings = FeatureValues.Parse(declared.Features, features);
            if (settings.IsFailure)
            {
                return settings.Error;
            }

            featuresChanged = !FeatureValues.HasSameEffect(existing, settings.Value, features.Values);
        }

        if (!featuresChanged
            && existing.Name == declared.Name.Trim()
            && existing.Description == Declared.Text(declared.Description)
            && existing.IsArchived == declared.IsArchived)
        {
            return ProvisioningChange.Unchanged;
        }

        var updated = await update.HandleAsync(
            existing.Id,
            new UpdatePlanRequest
            {
                Name = declared.Name,
                Description = declared.Description,
                IsArchived = declared.IsArchived,
                Features = featuresChanged ? declared.Features : null,
            },
            cancellationToken);
        return updated.IsSuccess ? ProvisioningChange.Updated : updated.Error;
    }
}
