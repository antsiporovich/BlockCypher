namespace ICMarkets.Domain.ValueObjects;

// BTC / LTC / Dash fee-per-kb family
public sealed record FeeRates(long? High, long? Medium, long? Low);
