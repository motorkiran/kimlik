using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Infrastructure.Persistence;

namespace Kimlik.Infrastructure.Outbox;

internal sealed class Outbox(KimlikDbContext context, OutboxMessageTypes types, TimeProvider timeProvider) : IOutbox
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue<TMessage>(TMessage message)
        where TMessage : class
    {
        var now = timeProvider.GetUtcNow();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = types.GetName(typeof(TMessage)),
            Payload = JsonSerializer.Serialize(message, SerializerOptions),
            OccurredAt = now,
            Status = OutboxMessageStatus.Pending,
            NextAttemptAt = now,
        });
    }
}
