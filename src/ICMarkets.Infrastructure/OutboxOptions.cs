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

    // broker credentials; defaults match the docker-compose RabbitMQ image's built-in
    // guest account. Override via Outbox__UserName / Outbox__Password (use a secret in k8s).
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";

    // Durable demo queue bound to the fanout exchange so published events have
    // somewhere to land (and show up in the RabbitMQ management UI). Empty =
    // declare nothing: real consumers own their queues, and with none bound the
    // mandatory publish is returned as unroutable and the outbox row stays
    // unprocessed.
    public string Queue { get; init; } = "icmarkets.snapshot-ingested.demo";
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;

    // how long a processed row stays around before OutboxPublisherWorker
    // purges it - keeps the table bounded under real traffic instead of
    // growing forever once Enabled = true.
    public int RetentionHours { get; init; } = 24;
}
