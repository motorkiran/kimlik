using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using DomainFeatureType = Kimlik.Domain.Plans.FeatureType;

namespace Kimlik.Application.Plans;

public sealed class ListFeaturesHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<FeatureResponse>>> HandleAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var features = context.Features.AsNoTracking();
        if (after is { } afterId)
        {
            features = features.Where(feature => feature.Id > afterId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(features.OrderBy(feature => feature.Id), limit, feature => feature.Id, cancellationToken);
        return new Page<FeatureResponse>([.. page.Select(feature => feature.ToResponse())], nextCursor);
    }
}

public sealed class GetFeatureHandler(IKimlikDbContext context)
{
    public async Task<Result<FeatureResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Features.AsNoTracking().SingleOrDefaultAsync(feature => feature.Id == id, cancellationToken) is { } feature
            ? feature.ToResponse()
            : PlanErrors.FeatureNotFound;
}

public sealed class CreateFeatureHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<FeatureResponse>> HandleAsync(CreateFeatureRequest request, CancellationToken cancellationToken)
    {
        var created = Feature.Create(request.Key, request.Name, request.Description, Enum.Parse<DomainFeatureType>(request.Type.ToString()), timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var feature = created.Value;
        if (await context.Features.AnyAsync(existing => existing.Key == feature.Key, cancellationToken))
        {
            return PlanErrors.FeatureExists;
        }

        context.Features.Add(feature);
        auditLog.Record(AuditActions.FeatureCreated, AuditSubject.Feature(feature.Id), new Dictionary<string, object?> { ["key"] = feature.Key });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return PlanErrors.FeatureExists;
        }

        return feature.ToResponse();
    }
}

/// <summary>Renames or describes a feature; its key and type are fixed, as code and plans depend on them.</summary>
public sealed class UpdateFeatureHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<FeatureResponse>> HandleAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken)
    {
        if (await context.Features.SingleOrDefaultAsync(feature => feature.Id == id, cancellationToken) is not { } feature)
        {
            return PlanErrors.FeatureNotFound;
        }

        if (!request.HasName && !request.HasDescription)
        {
            return feature.ToResponse();
        }

        var updated = feature.Update(
            request.HasName ? request.Name ?? string.Empty : feature.Name,
            request.HasDescription ? request.Description : feature.Description,
            timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        auditLog.Record(AuditActions.FeatureUpdated, AuditSubject.Feature(id));
        await context.SaveChangesAsync(cancellationToken);
        return feature.ToResponse();
    }
}

/// <summary>Deletes a feature and its value in every plan; subscribers lose it at once.</summary>
public sealed class DeleteFeatureHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.Features.SingleOrDefaultAsync(feature => feature.Id == id, cancellationToken) is not { } feature)
        {
            return PlanErrors.FeatureNotFound;
        }

        context.Features.Remove(feature);
        auditLog.Record(AuditActions.FeatureDeleted, AuditSubject.Feature(id), new Dictionary<string, object?> { ["key"] = feature.Key });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
