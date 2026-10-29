namespace ICMarkets.Infrastructure.Messaging;

// Transactional outbox: written in the SAME SaveChanges call as the
// BlockchainSnapshot that raised the domain event (see AppDbContext), so a
// crash between "snapshot saved" and "event published to RabbitMQ" can never
// lose the event. OutboxPublisherWorker drains this table independently of
// whether RabbitMQ was reachable at ingestion time.
public class OutboxMessage
{
    public long Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private OutboxMessage() { } // EF Core

    public static OutboxMessage Create(string type, string payload, DateTimeOffset occurredAt) =>
        new() { Type = type, Payload = payload, OccurredAt = occurredAt };

    public void MarkProcessed(DateTimeOffset at) => ProcessedAt = at;
}
