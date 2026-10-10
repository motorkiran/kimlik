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
        builder.Property(feature => feature.IsMetered).HasDefaultValue(false);
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
        builder.Property(plan => plan.Kind).HasConversion<string>().HasMaxLength(16);
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

        builder.HasMany(subscription => subscription.AddOns).WithOne().HasForeignKey(addOn => addOn.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(subscription => subscription.AddOns).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(subscription => subscription.FeatureOverrides).WithOne().HasForeignKey(value => value.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(subscription => subscription.FeatureOverrides).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(subscription => subscription.HasCustomEntitlements);
    }
}

internal sealed class SubscriptionAddOnConfiguration : IEntityTypeConfiguration<SubscriptionAddOn>
{
    public void Configure(EntityTypeBuilder<SubscriptionAddOn> builder)
    {
        builder.HasKey(addOn => new { addOn.SubscriptionId, addOn.PlanId });

        // An add-on with subscriptions is archived rather than deleted, as a plan is.
        builder.HasOne<Plan>().WithMany().HasForeignKey(addOn => addOn.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(addOn => addOn.PlanId);
    }
}

internal sealed class SubscriptionFeatureOverrideConfiguration : IEntityTypeConfiguration<SubscriptionFeatureOverride>
{
    public void Configure(EntityTypeBuilder<SubscriptionFeatureOverride> builder)
    {
        builder.HasKey(value => new { value.SubscriptionId, value.FeatureId });
        builder.HasOne<Feature>().WithMany().HasForeignKey(value => value.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => value.FeatureId);
    }
}

internal sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("usage_counters", table => table.HasCheckConstraint("ck_usage_counters_one_subscriber", "(user_id IS NULL) <> (organization_id IS NULL)"));
        builder.Property(counter => counter.Id).ValueGeneratedNever();
        builder.HasOne<User>().WithMany().HasForeignKey(counter => counter.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(counter => counter.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(counter => counter.FeatureId).OnDelete(DeleteBehavior.Cascade);

        // One counter per subscriber, feature and month; the counting upserts against these.
        builder.HasIndex(counter => new { counter.UserId, counter.FeatureId, counter.Period }, "ix_usage_counters_user").IsUnique().HasFilter("user_id IS NOT NULL");
        builder.HasIndex(counter => new { counter.OrganizationId, counter.FeatureId, counter.Period }, "ix_usage_counters_organization")
            .IsUnique().HasFilter("organization_id IS NOT NULL");
        builder.HasIndex(counter => counter.FeatureId);
        builder.HasIndex(counter => counter.Period);
    }
}

internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records", table => table.HasCheckConstraint("ck_usage_records_one_subscriber", "(user_id IS NULL) <> (organization_id IS NULL)"));
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Key).HasMaxLength(UsageRecord.KeyMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(record => record.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(record => record.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.UserId, record.Key }, "ix_usage_records_user").IsUnique().HasFilter("user_id IS NOT NULL");
        builder.HasIndex(record => new { record.OrganizationId, record.Key }, "ix_usage_records_organization").IsUnique().HasFilter("organization_id IS NOT NULL");
        builder.HasIndex(record => record.CreatedAt);
    }
}
