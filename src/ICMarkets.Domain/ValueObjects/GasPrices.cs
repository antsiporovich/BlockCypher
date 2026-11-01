namespace ICMarkets.Domain.ValueObjects;

// ETH gas-price family. BaseFee/*PriorityFee confirmed present on live ETH
// responses during Phase 0 recon (beyond the three documented gas_price
// fields) - all long, since high_gas_price observed > Int32.MaxValue.
// None of these can be negative, so the record guards that invariant at construction: a GasPrices
// can never exist in a negative state from any path (the adapter also rejects negative upstream
// values before they reach here - defence in depth). Zero stays valid.
public sealed record GasPrices(
    long? High,
    long? Medium,
    long? Low,
    long? BaseFee,
    long? HighPriorityFee,
    long? MediumPriorityFee,
    long? LowPriorityFee)
{
    public long? High { get; } = Guard(High, nameof(High));
    public long? Medium { get; } = Guard(Medium, nameof(Medium));
    public long? Low { get; } = Guard(Low, nameof(Low));
    public long? BaseFee { get; } = Guard(BaseFee, nameof(BaseFee));
    public long? HighPriorityFee { get; } = Guard(HighPriorityFee, nameof(HighPriorityFee));
    public long? MediumPriorityFee { get; } = Guard(MediumPriorityFee, nameof(MediumPriorityFee));
    public long? LowPriorityFee { get; } = Guard(LowPriorityFee, nameof(LowPriorityFee));

    private static long? Guard(long? value, string name)
    {
        if (value is < 0)
            throw new ArgumentOutOfRangeException(name, value, "A gas price must not be negative.");
        return value;
    }
}
