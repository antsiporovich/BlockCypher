namespace ICMarkets.Domain.ValueObjects;

// BTC / LTC / Dash fee-per-kb family. A fee cannot be negative, so the record guards that
// invariant at construction: a FeeRates can never exist in a negative state from any path (the
// adapter also rejects negative upstream values before they reach here - defence in depth). Zero
// stays valid (a low-fee tier of 0 is plausible).
public sealed record FeeRates(long? High, long? Medium, long? Low)
{
    public long? High { get; } = Guard(High, nameof(High));
    public long? Medium { get; } = Guard(Medium, nameof(Medium));
    public long? Low { get; } = Guard(Low, nameof(Low));

    private static long? Guard(long? value, string name)
    {
        if (value is < 0)
            throw new ArgumentOutOfRangeException(name, value, "A fee rate must not be negative.");
        return value;
    }
}
