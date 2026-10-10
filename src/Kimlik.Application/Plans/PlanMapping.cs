using Kimlik.Application.Abstractions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using FeatureType = Kimlik.Contracts.Management.FeatureType;
using PlanKind = Kimlik.Contracts.Management.PlanKind;

namespace Kimlik.Application.Plans;

internal static class PlanMapping
{
    public static FeatureResponse ToResponse(this Feature feature) => new(
        feature.Id, feature.Key, feature.Name, feature.Description, Enum.Parse<FeatureType>(feature.Type.ToString()), feature.CreatedAt, feature.UpdatedAt);

    public static PlanResponse ToResponse(this Plan plan, IEnumerable<Feature> features) => new(
        plan.Id,
        plan.Key,
        plan.Name,
        plan.Description,
        plan.IsArchived,
        FeatureValues.Describe(plan, features),
        plan.CreatedAt,
        plan.UpdatedAt,
        Enum.Parse<PlanKind>(plan.Kind.ToString()));

    public static Task<List<Feature>> AllFeaturesAsync(this IKimlikDbContext context, CancellationToken cancellationToken) =>
        context.Features.AsNoTracking().ToListAsync(cancellationToken);

    public static async Task<Dictionary<string, Feature>> FeaturesByKeyAsync(this IKimlikDbContext context, CancellationToken cancellationToken) =>
        (await context.AllFeaturesAsync(cancellationToken)).ToDictionary(feature => feature.Key, StringComparer.Ordinal);
}
