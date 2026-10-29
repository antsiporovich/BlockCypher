namespace ICMarkets.Infrastructure.Messaging;

// Deliberately narrow - a type tag and a JSON payload, not a typed method per
// event. The outbox table stores exactly these two columns, so the publisher
// contract mirrors it instead of needing a mapper in between.
public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken ct);
}
