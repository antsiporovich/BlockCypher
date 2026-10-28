namespace ICMarkets.Domain;

// single source of truth for the network enum <-> string key mapping used in
// routes, DB storage and config. Keeps the 5-network list defined once.
public static class NetworkKey
{
    private static readonly Dictionary<Network, string> ToKeyMap = new()
    {
        [Network.BtcMain] = "btc-main",
        [Network.EthMain] = "eth-main",
        [Network.DashMain] = "dash-main",
        [Network.BtcTest3] = "btc-test3",
        [Network.LtcMain] = "ltc-main",
    };

    private static readonly Dictionary<string, Network> FromKeyMap =
        ToKeyMap.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllKeys { get; } = ToKeyMap.Values.ToList();

    public static string ToKey(Network network) => ToKeyMap[network];

    public static bool TryParse(string? key, out Network network)
    {
        network = default;
        return key is not null && FromKeyMap.TryGetValue(key, out network);
    }

    public static Network Parse(string key) =>
        TryParse(key, out var network)
            ? network
            : throw new ArgumentException($"Unknown network key '{key}'.", nameof(key));
}
