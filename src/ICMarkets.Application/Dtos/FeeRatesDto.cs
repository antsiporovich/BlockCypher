namespace ICMarkets.Application.Dtos;

/// <summary>Fee-per-kilobyte tiers, in satoshis (or the chain's smallest unit). BTC/LTC/Dash only.</summary>
/// <param name="High">High-priority fee rate.</param>
/// <param name="Medium">Medium-priority fee rate.</param>
/// <param name="Low">Low-priority fee rate.</param>
public sealed record FeeRatesDto(long? High, long? Medium, long? Low);
