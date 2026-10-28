namespace ICMarkets.Domain.Events;

public sealed record SnapshotIngested(Network Network, long Height, DateTimeOffset CreatedAt) : IDomainEvent;
