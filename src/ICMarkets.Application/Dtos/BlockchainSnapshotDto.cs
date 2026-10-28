namespace ICMarkets.Application.Dtos;

// RawJson deliberately excluded - it's stored for audit/compliance, not for
// bloating every API response.
public sealed record BlockchainSnapshotDto(
    int Id,
    string Network,
    string Name,
    long Height,
    string Hash,
    DateTimeOffset Time,
    string? LatestUrl,
    string? PreviousHash,
    string? PreviousUrl,
    int PeerCount,
    int UnconfirmedCount,
    FeeRatesDto? FeeRates,
    GasPricesDto? GasPrices,
    long? LastForkHeight,
    string? LastForkHash,
    DateTimeOffset CreatedAt);
