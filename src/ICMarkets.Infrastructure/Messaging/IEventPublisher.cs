namespace ICMarkets.Infrastructure.Messaging;

// Deliberately narrow - a stable id, a type tag and a JSON payload, not a typed
// method per event. The outbox table stores exactly these (id, type, payload),
// so the publisher contract mirrors it instead of needing a mapper in between.
//
// Contract: PublishAsync completes ONLY when the broker has taken responsibility
// for the message (publisher-confirmed, routed to at least one queue). Any other
// outcome - broker down, nack, unroutable - throws, so the caller keeps the row
// and retries. Delivery is therefore at-least-once; consumers must be idempotent
// on messageId (a crash between the broker's ack and marking the row processed
// publishes the same messageId again).
public interface IEventPublisher
{
    Task PublishAsync(string messageId, string type, string payload, CancellationToken ct);
}
