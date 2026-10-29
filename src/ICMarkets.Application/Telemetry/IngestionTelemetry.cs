using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ICMarkets.Application.Telemetry;

// Domain telemetry for the ingestion pipeline - custom spans and metrics on
// top of the framework-default ASP.NET Core/HttpClient/EF Core instrumentation
// wired in Api/Program.cs. ActivitySource/Meter are plain BCL types (System.
// Diagnostics[.Metrics]), not an OpenTelemetry SDK type, so Application stays
// free of any OpenTelemetry package reference - only Api (the composition
// root) references the SDK and registers these names via AddSource/AddMeter.
public static class IngestionTelemetry
{
    public const string ActivitySourceName = "ICMarkets.Ingestion";
    public const string MeterName = "ICMarkets.Ingestion";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> SnapshotsIngested = Meter.CreateCounter<long>(
        "icmarkets.snapshots.ingested",
        description: "Snapshots successfully fetched and persisted, by network.");

    public static readonly Counter<long> IngestionFailures = Meter.CreateCounter<long>(
        "icmarkets.ingestion.failures",
        description: "Ingestion failures (fetch or persist), by network.");

    public static readonly Histogram<double> FetchDuration = Meter.CreateHistogram<double>(
        "icmarkets.fetch.duration",
        unit: "ms",
        description: "BlockCypher fetch duration per network.");

    public static KeyValuePair<string, object?> NetworkTag(string networkKey) => new("network", networkKey);
}
