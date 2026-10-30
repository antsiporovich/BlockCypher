using ICMarkets.Domain.Events;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.Domain.Entities;

public class BlockchainSnapshot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public int Id { get; private set; }
    public Network Network { get; private set; }
    public string Name { get; private set; } = null!;
    public long Height { get; private set; }
    public string Hash { get; private set; } = null!;
    public DateTimeOffset Time { get; private set; }
    public string? LatestUrl { get; private set; }
    public string? PreviousHash { get; private set; }
    public string? PreviousUrl { get; private set; }
    public int PeerCount { get; private set; }
    public int UnconfirmedCount { get; private set; }
    public FeeRates? FeeRates { get; private set; }
    public GasPrices? GasPrices { get; private set; }
    public long? LastForkHeight { get; private set; }
    public string? LastForkHash { get; private set; }
    public string RawJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;
    public void ClearDomainEvents() => _domainEvents.Clear();

    private BlockchainSnapshot() { } // EF Core

    // factory takes primitives + already-built value objects, never an
    // Application-layer DTO - Domain must not depend on Application.
    public static BlockchainSnapshot Create(
        Network network,
        string name,
        long height,
        string hash,
        DateTimeOffset time,
        string? latestUrl,
        string? previousHash,
        string? previousUrl,
        int peerCount,
        int unconfirmedCount,
        FeeRates? feeRates,
        GasPrices? gasPrices,
        long? lastForkHeight,
        string? lastForkHash,
        string rawJson,
        DateTimeOffset createdAt)
    {
        // The entity guards its own invariants rather than trusting every caller: the external
        // payload is validated in the adapter, but a snapshot of default values must be
        // impossible to construct from any other path too.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(peerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unconfirmedCount);
        // a fork height is a block height - it cannot be negative (the fee/gas tiers guard
        // themselves inside their value objects; this primitive lives directly on the entity).
        if (lastForkHeight is { } forkHeight)
            ArgumentOutOfRangeException.ThrowIfNegative(forkHeight);

        var snapshot = new BlockchainSnapshot
        {
            Network = network,
            Name = name,
            Height = height,
            Hash = hash,
            Time = time,
            LatestUrl = latestUrl,
            PreviousHash = previousHash,
            PreviousUrl = previousUrl,
            PeerCount = peerCount,
            UnconfirmedCount = unconfirmedCount,
            FeeRates = feeRates,
            GasPrices = gasPrices,
            LastForkHeight = lastForkHeight,
            LastForkHash = lastForkHash,
            RawJson = rawJson,
            CreatedAt = createdAt,
        };

        snapshot._domainEvents.Add(new SnapshotIngested(network, height, createdAt));
        return snapshot;
    }
}
