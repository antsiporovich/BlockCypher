namespace ICMarkets.Infrastructure;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public bool Enabled { get; init; } = true;

    // kept modest - BlockCypher rate-limits unauthenticated callers.
    public int IntervalSeconds { get; init; } = 300;
}
