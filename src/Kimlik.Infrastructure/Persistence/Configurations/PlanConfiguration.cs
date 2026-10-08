using Kimlik.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    public void Configure(EntityTypeBuilder<Feature> builder)
    {
        builder.Property(feature => feature.Id).ValueGeneratedNever();
        builder.Property(feature => feature.Key).HasMaxLength(Feature.KeyMaxLength);
        builder.Property(feature => feature.Name).HasMaxLength(Feature.NameMaxLength);
        builder.Property(feature => feature.Description).HasMaxLength(Feature.DescriptionMaxLength);
        builder.Property(feature => feature.Type).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(feature => feature.Key).IsUnique();
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.Property(plan => plan.Key).HasMaxLength(Feature.KeyMaxLength);
        builder.Property(plan => plan.Name).HasMaxLength(Feature.NameMaxLength);
        builder.Property(plan => plan.Description).HasMaxLength(Feature.DescriptionMaxLength);
        builder.HasIndex(plan => plan.Key).IsUnique();

        builder.HasMany(plan => plan.Features).WithOne().HasForeignKey(value => value.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(plan => plan.Features).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.HasKey(value => new { value.PlanId, value.FeatureId });
        builder.HasOne<Feature>().WithMany().HasForeignKey(value => value.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => value.FeatureId);
    }
}
