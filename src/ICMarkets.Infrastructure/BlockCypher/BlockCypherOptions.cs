using ICMarkets.Domain;

namespace ICMarkets.Infrastructure.BlockCypher;

public sealed class BlockCypherOptions
{
    public const string SectionName = "BlockCypher";

    public required string BaseUrl { get; init; }
    public required Dictionary<Network, string> Networks { get; init; }

    // Optional - BlockCypher works unauthenticated at low volume; set this to
    // raise the rate limit. Never commit a real token - set via the
    // BlockCypher__Token env var or user-secrets, not appsettings.json.
    public string? Token { get; init; }

    // Hard budget for ONE fetch (headers + body, retries included). HttpClient's own
    // Timeout is disabled (see DependencyInjection) because it surfaces as a
    // TaskCanceledException indistinguishable from real cancellation; this budget is
    // enforced by BlockCypherClient and reported as a TimeoutException instead.
    // Keep it above the resilience pipeline's total request timeout (30s default).
    public int FetchTimeoutSeconds { get; init; } = 45;

    // Time-based cap on REAL HTTP attempts (retries included) to BlockCypher, shared by
    // every caller in the process (all /ingest requests and the background worker).
    // BlockCypher's anonymous limit is about 3 requests/second. Per process: several
    // replicas would each get their own budget and need a distributed limiter.
    public int RequestsPerSecond { get; init; } = 3;
}
