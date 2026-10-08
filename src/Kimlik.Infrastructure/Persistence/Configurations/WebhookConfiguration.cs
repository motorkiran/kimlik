using Kimlik.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.Property(endpoint => endpoint.Id).ValueGeneratedNever();
        builder.Property(endpoint => endpoint.Url).HasMaxLength(WebhookEndpoint.UrlMaxLength);
        builder.Property(endpoint => endpoint.Description).HasMaxLength(WebhookEndpoint.DescriptionMaxLength);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.EventType).HasMaxLength(64);
        builder.Property(delivery => delivery.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(delivery => delivery.ResponseBody).HasMaxLength(WebhookDelivery.ResponseBodyMaxLength);
        builder.Property(delivery => delivery.Error).HasMaxLength(WebhookDelivery.ErrorMaxLength);

        // A deleted endpoint takes its delivery log with it.
        builder.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(delivery => delivery.EndpointId).OnDelete(DeleteBehavior.Cascade);

        // The sender looks for pending deliveries that are due.
        builder.HasIndex(delivery => delivery.NextAttemptAt).HasFilter("status = 'Pending'");
        builder.HasIndex(delivery => new { delivery.EndpointId, delivery.Id });

        // Fanning an event out checks whether it already was, should the outbox deliver it twice.
        builder.HasIndex(delivery => delivery.EventId);
        builder.HasIndex(delivery => delivery.CreatedAt);
    }
}
