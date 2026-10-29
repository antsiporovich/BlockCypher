using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.PerformanceTests;

// Shared sample data for both benchmark classes - a realistic BTC snapshot
// (FeeRates populated, GasPrices null), built once and reused so each
// benchmark measures only the code under test, not fixture construction.
internal static class SnapshotFixtures
{
    public static BlockchainSnapshot OneBtcSnapshot() => BlockchainSnapshot.Create(
        Network.BtcMain,
        name: "BTC.main",
        height: 973_576,
        hash: "0000000000000000000a1b2c3d4e5f60718293a4b5c6d7e8f9001122334455",
        time: DateTimeOffset.UtcNow,
        latestUrl: "https://api.blockcypher.com/v1/btc/main/blocks/973576",
        previousHash: "0000000000000000000a1b2c3d4e5f60718293a4b5c6d7e8f9001122334454",
        previousUrl: "https://api.blockcypher.com/v1/btc/main/blocks/973575",
        peerCount: 250,
        unconfirmedCount: 4821,
        feeRates: new FeeRates(High: 42, Medium: 28, Low: 12),
        gasPrices: null,
        lastForkHeight: null,
        lastForkHash: null,
        rawJson: "{}",
        createdAt: DateTimeOffset.UtcNow);

    // mirrors the default `take` of GetChainHistoryQuery/history endpoints.
    public static List<BlockchainSnapshot> Snapshots(int count) =>
        Enumerable.Range(0, count).Select(_ => OneBtcSnapshot()).ToList();
}
