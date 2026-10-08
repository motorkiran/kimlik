using Kimlik.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(16);

        // The dispatcher only ever looks for pending messages that are due.
        builder.HasIndex(message => message.NextAttemptAt)
            .HasFilter($"status = '{nameof(OutboxMessageStatus.Pending)}'");
    }
}
