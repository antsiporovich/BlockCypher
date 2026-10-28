using ICMarkets.Domain;

namespace ICMarkets.Infrastructure.BlockCypher;

public sealed class BlockCypherOptions
{
    public const string SectionName = "BlockCypher";

    public required string BaseUrl { get; init; }
    public required Dictionary<Network, string> Networks { get; init; }
}
