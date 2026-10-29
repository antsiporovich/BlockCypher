namespace ICMarkets.Infrastructure;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    // off by default (local/SQLite dev, functional tests) - no RabbitMQ
    // required unless explicitly turned on, same shape as IngestionOptions.
    public bool Enabled { get; init; }

    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string Exchange { get; init; } = "icmarkets.snapshot-ingested";
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;
}
