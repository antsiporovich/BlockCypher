using System.Text.Json.Serialization;

namespace ICMarkets.Application.Dtos;

// superset DTO: fee-per-kb family (BTC/LTC/Dash) + gas-price family (ETH),
// both nullable. ETH's base_fee/*_priority_fee confirmed present on live
// responses during Phase 0 recon.
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

    [JsonPropertyName("high_gas_price")] public long? HighGasPrice { get; set; }
    [JsonPropertyName("medium_gas_price")] public long? MediumGasPrice { get; set; }
    [JsonPropertyName("low_gas_price")] public long? LowGasPrice { get; set; }
    [JsonPropertyName("base_fee")] public long? BaseFee { get; set; }
    [JsonPropertyName("high_priority_fee")] public long? HighPriorityFee { get; set; }
    [JsonPropertyName("medium_priority_fee")] public long? MediumPriorityFee { get; set; }
    [JsonPropertyName("low_priority_fee")] public long? LowPriorityFee { get; set; }

    [JsonPropertyName("last_fork_height")] public long? LastForkHeight { get; set; }
    [JsonPropertyName("last_fork_hash")] public string? LastForkHash { get; set; }
}
