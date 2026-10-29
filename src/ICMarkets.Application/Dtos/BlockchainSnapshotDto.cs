namespace ICMarkets.Application.Dtos;

/// <summary>
/// A stored network summary at a point in time. <see cref="FeeRates"/> is populated for the
/// BTC-family chains (BTC/LTC/Dash), <see cref="GasPrices"/> for ETH - exactly one of the two
/// is non-null depending on <see cref="Network"/>.
/// </summary>
/// <param name="Id">Row identifier.</param>
/// <param name="Network">Network key, e.g. <c>btc-main</c>.</param>
/// <param name="Name">The chain name as reported by BlockCypher, e.g. <c>BTC.main</c>.</param>
/// <param name="Height">Current block height at the time of the snapshot.</param>
/// <param name="Hash">Hash of the latest block.</param>
/// <param name="Time">Timestamp of the latest block, as reported by BlockCypher.</param>
/// <param name="LatestUrl">BlockCypher API URL for the latest block.</param>
/// <param name="PreviousHash">Hash of the block before the latest one.</param>
/// <param name="PreviousUrl">BlockCypher API URL for the previous block.</param>
/// <param name="PeerCount">Number of peers BlockCypher's node was connected to.</param>
/// <param name="UnconfirmedCount">Number of unconfirmed transactions in the mempool.</param>
/// <param name="FeeRates">Fee-per-kb tiers (BTC/LTC/Dash chains only).</param>
/// <param name="GasPrices">Gas price tiers (ETH chains only).</param>
/// <param name="LastForkHeight">Height of the most recent chain fork, if any.</param>
/// <param name="LastForkHash">Hash of the most recent chain fork, if any.</param>
/// <param name="CreatedAt">When this snapshot was ingested (UTC).</param>
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
