using System.Text.Json.Serialization;

namespace ICMarkets.Application.Dtos;

// fee-per-kb family only for now (btc/main). ETH's gas-price fields arrive in Phase 4.
public sealed class BlockCypherChainInfoDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("height")] public long Height { get; set; }
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("latest_url")] public string? LatestUrl { get; set; }
    [JsonPropertyName("previous_hash")] public string? PreviousHash { get; set; }
    [JsonPropertyName("previous_url")] public string? PreviousUrl { get; set; }
    [JsonPropertyName("peer_count")] public int PeerCount { get; set; }
    [JsonPropertyName("unconfirmed_count")] public int UnconfirmedCount { get; set; }
    [JsonPropertyName("high_fee_per_kb")] public long? HighFeePerKb { get; set; }
    [JsonPropertyName("medium_fee_per_kb")] public long? MediumFeePerKb { get; set; }
    [JsonPropertyName("low_fee_per_kb")] public long? LowFeePerKb { get; set; }
    [JsonPropertyName("last_fork_height")] public long? LastForkHeight { get; set; }
    [JsonPropertyName("last_fork_hash")] public string? LastForkHash { get; set; }
}
