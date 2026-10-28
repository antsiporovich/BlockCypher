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
}
