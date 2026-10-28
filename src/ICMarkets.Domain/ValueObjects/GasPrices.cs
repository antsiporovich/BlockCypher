namespace ICMarkets.Domain.ValueObjects;

// ETH gas-price family. BaseFee/*PriorityFee confirmed present on live ETH
// responses during Phase 0 recon (beyond the three documented gas_price
// fields) - all long, since high_gas_price observed > Int32.MaxValue.
public sealed record GasPrices(
    long? High,
    long? Medium,
    long? Low,
    long? BaseFee,
    long? HighPriorityFee,
    long? MediumPriorityFee,
    long? LowPriorityFee);
