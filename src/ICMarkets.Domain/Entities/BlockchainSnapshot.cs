namespace ICMarkets.Domain.Entities;

// ponytail: flat/rough on purpose - this is the Phase 2 walking-skeleton shape.
// Phase 4 upgrades to a rich entity (static factory, FeeRates/GasPrices value objects, domain event).
public class BlockchainSnapshot
{
    public int Id { get; set; }
    public required string Network { get; set; }       // our key, e.g. "btc-main"
    public required string Name { get; set; }           // BlockCypher's own "name", e.g. "BTC.main"
    public long Height { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset Time { get; set; }
    public string? LatestUrl { get; set; }
    public string? PreviousHash { get; set; }
    public string? PreviousUrl { get; set; }
    public int PeerCount { get; set; }
    public int UnconfirmedCount { get; set; }

    // fee-per-kb family (BTC/LTC/Dash). Gas-price family (ETH) added in Phase 4.
    public long? HighFeePerKb { get; set; }
    public long? MediumFeePerKb { get; set; }
    public long? LowFeePerKb { get; set; }

    public long? LastForkHeight { get; set; }
    public string? LastForkHash { get; set; }

    public required string RawJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
