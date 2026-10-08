using Kimlik.Domain.Organizations;
using Kimlik.Domain.Plans;
using Kimlik.Domain.Users;
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

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    /// <summary>The statuses of a subscription that is still current, as stored.</summary>
    private const string CurrentStatus = "status <> 'Expired'";

    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_subscriptions_one_subscriber", "(user_id IS NULL) <> (organization_id IS NULL)"));
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(subscription => subscription.ExternalReference).HasMaxLength(Subscription.ExternalReferenceMaxLength);
        builder.Ignore(subscription => subscription.Subscriber);
        builder.Ignore(subscription => subscription.EndsAt);

        // A plan with subscriptions is archived rather than deleted.
        builder.HasOne<Plan>().WithMany().HasForeignKey(subscription => subscription.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(subscription => subscription.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(subscription => subscription.OrganizationId).OnDelete(DeleteBehavior.Cascade);

        // One current subscription per subscriber; ended ones stay as history.
        builder.HasIndex(subscription => subscription.UserId, "ix_subscriptions_current_user").IsUnique().HasFilter($"{CurrentStatus} AND user_id IS NOT NULL");
        builder.HasIndex(subscription => subscription.OrganizationId, "ix_subscriptions_current_organization").IsUnique().HasFilter($"{CurrentStatus} AND organization_id IS NOT NULL");
        builder.HasIndex(subscription => subscription.UserId);
        builder.HasIndex(subscription => subscription.OrganizationId);
        builder.HasIndex(subscription => subscription.PlanId);
        builder.HasIndex(subscription => subscription.Status);
    }
}
