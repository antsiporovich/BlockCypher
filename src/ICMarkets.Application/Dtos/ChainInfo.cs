using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.Application.Dtos;

/// <summary>
/// Provider-neutral summary of a chain's current state - what the ingestion use case needs,
/// independent of any external API's wire format. An adapter (e.g. BlockCypher) maps its own
/// contract into this. Exactly one of <see cref="FeeRates"/> / <see cref="GasPrices"/> is
/// normally set, depending on the network family.
/// </summary>
public sealed record ChainInfo(
    string Name,
    long Height,
    string Hash,
    DateTimeOffset Time,
    string? LatestUrl,
    string? PreviousHash,
    string? PreviousUrl,
    int PeerCount,
    int UnconfirmedCount,
    FeeRates? FeeRates,
    GasPrices? GasPrices,
    long? LastForkHeight,
    string? LastForkHash);
