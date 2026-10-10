using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Plans;

/// <summary>Lists plans in creation order; archived ones only when asked.</summary>
public sealed record ListPlansQuery(bool? Archived, string? Cursor, int? Limit);

public sealed class ListPlansHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<PlanResponse>>> HandleAsync(ListPlansQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var plans = context.Plans.AsNoTracking().Include(plan => plan.Features).AsQueryable();
        if (after is { } afterId)
        {
            plans = plans.Where(plan => plan.Id > afterId);
        }

        if (query.Archived is { } archived)
        {
            plans = plans.Where(plan => plan.IsArchived == archived);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(plans.OrderBy(plan => plan.Id), query.Limit, plan => plan.Id, cancellationToken);
        var features = await context.AllFeaturesAsync(cancellationToken);
        return new Page<PlanResponse>([.. page.Select(plan => plan.ToResponse(features))], nextCursor);
    }
}

public sealed class GetPlanHandler(IKimlikDbContext context)
{
    public async Task<Result<PlanResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Plans.AsNoTracking().Include(plan => plan.Features).SingleOrDefaultAsync(plan => plan.Id == id, cancellationToken) is { } plan
            ? plan.ToResponse(await context.AllFeaturesAsync(cancellationToken))
            : PlanErrors.PlanNotFound;
}

public sealed class CreatePlanHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<PlanResponse>> HandleAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var created = Plan.Create(request.Key, request.Name, request.Description, Enum.Parse<Domain.Plans.PlanKind>(request.Kind.ToString()), now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var plan = created.Value;
        var features = await context.FeaturesByKeyAsync(cancellationToken);
        var settings = FeatureValues.Parse(request.Features, features);
        if (settings.IsFailure)
        {
            return settings.Error;
        }

        var set = plan.SetFeatures(settings.Value, now);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (await context.Plans.AnyAsync(existing => existing.Key == plan.Key, cancellationToken))
        {
            return PlanErrors.PlanExists;
        }

        context.Plans.Add(plan);
        auditLog.Record(AuditActions.PlanCreated, AuditSubject.Plan(plan.Id), new Dictionary<string, object?> { ["key"] = plan.Key });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return PlanErrors.PlanExists;
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // A feature was deleted while the plan was being saved.
            return PlanErrors.UnknownFeature;
        }

        return plan.ToResponse(features.Values);
    }
}

/// <summary>Changes a plan; its key is fixed. New feature values apply to its subscribers at once.</summary>
public sealed class UpdatePlanHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<PlanResponse>> HandleAsync(Guid id, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        if (await context.Plans.Include(plan => plan.Features).SingleOrDefaultAsync(plan => plan.Id == id, cancellationToken) is not { } plan)
        {
            return PlanErrors.PlanNotFound;
        }

        var features = await context.FeaturesByKeyAsync(cancellationToken);
        if (!request.HasName && !request.HasDescription && request.IsArchived is null && request.Features is null)
        {
            return plan.ToResponse(features.Values);
        }

        var now = timeProvider.GetUtcNow();
        var updated = plan.Update(
            request.HasName ? request.Name ?? string.Empty : plan.Name,
            request.HasDescription ? request.Description : plan.Description,
            request.IsArchived ?? plan.IsArchived,
            now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (request.Features is not null)
        {
            var settings = FeatureValues.Parse(request.Features, features);
            if (settings.IsFailure)
            {
                return settings.Error;
            }

            var set = plan.SetFeatures(settings.Value, now);
            if (set.IsFailure)
            {
                return set.Error;
            }
        }

        auditLog.Record(AuditActions.PlanUpdated, AuditSubject.Plan(id));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // A feature was deleted while the plan was being saved.
            return PlanErrors.UnknownFeature;
        }

        return plan.ToResponse(features.Values);
    }
}

public sealed class DeletePlanHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.Plans.SingleOrDefaultAsync(plan => plan.Id == id, cancellationToken) is not { } plan)
        {
            return PlanErrors.PlanNotFound;
        }

        // Subscriptions to it, ended ones included, or to it as an add-on, keep it.
        if (await context.Subscriptions.AnyAsync(subscription => subscription.PlanId == id || subscription.AddOns.Any(addOn => addOn.PlanId == id), cancellationToken))
        {
            return PlanErrors.PlanInUse;
        }

        context.Plans.Remove(plan);
        auditLog.Record(AuditActions.PlanDeleted, AuditSubject.Plan(id), new Dictionary<string, object?> { ["key"] = plan.Key });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // A subscription took it while it was being deleted.
            return PlanErrors.PlanInUse;
        }

        return Result.Success();
    }
}
