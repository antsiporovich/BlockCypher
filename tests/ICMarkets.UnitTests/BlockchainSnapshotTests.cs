using AwesomeAssertions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.Events;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.UnitTests.Domain;

// The factory is the only way to build a snapshot, so it is where the invariants live: a
// snapshot of default values (empty name/hash, negative height) must be impossible to
// construct no matter which caller forgot to validate.
public sealed class BlockchainSnapshotTests
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BlockchainSnapshot Create(
        string name = "BTC.main", string hash = "h", long height = 1, string rawJson = "{}",
        int peerCount = 0, int unconfirmedCount = 0) =>
        BlockchainSnapshot.Create(
            Network.BtcMain, name, height, hash, T, null, null, null, peerCount, unconfirmedCount,
            new FeeRates(1, 1, 1), null, null, null, rawJson, T);

    [Fact]
    public void Create_ValidInput_BuildsTheSnapshot_AndRaisesTheIngestedEvent()
    {
        var snapshot = Create(height: 0); // genesis-like height 0 is allowed

        snapshot.Name.Should().Be("BTC.main");
        snapshot.DomainEvents.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string name) =>
        ((Action)(() => Create(name: name))).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankHash_Throws(string hash) =>
        ((Action)(() => Create(hash: hash))).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_BlankRawPayload_Throws() =>
        ((Action)(() => Create(rawJson: " "))).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_NegativeHeight_Throws() =>
        ((Action)(() => Create(height: -1))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Create_NegativeCounts_Throw()
    {
        ((Action)(() => Create(peerCount: -1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => Create(unconfirmedCount: -1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_NegativeForkHeight_Throws() =>
        ((Action)(() => BlockchainSnapshot.Create(
            Network.BtcMain, "BTC.main", 1, "h", T, null, null, null, 0, 0,
            new FeeRates(1, 1, 1), null, lastForkHeight: -1, null, "{}", T)))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Create_NonNegativeForkHeight_IsAllowed() =>
        BlockchainSnapshot.Create(
            Network.BtcMain, "BTC.main", 1, "h", T, null, null, null, 0, 0,
            new FeeRates(1, 1, 1), null, lastForkHeight: 100, null, "{}", T)
            .LastForkHeight.Should().Be(100);

    [Fact]
    public void Create_EthWithGasPricesOnly_BuildsTheSnapshot_AndRaisesTheIngestedEvent()
    {
        var snapshot = CreateFor(Network.EthMain, fees: null, gas: new GasPrices(1, 1, 1, null, null, null, null));

        snapshot.Network.Should().Be(Network.EthMain);
        snapshot.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SnapshotIngested>();
    }

    [Fact]
    public void Create_EthWithFeeRatesAndNoGas_Throws() =>
        ((Action)(() => CreateFor(Network.EthMain, new FeeRates(1, 1, 1), gas: null))).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_EthWithBothFamilies_Throws() =>
        ((Action)(() => CreateFor(Network.EthMain, new FeeRates(1, 1, 1), new GasPrices(1, 1, 1, null, null, null, null))))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Create_EthWithNeitherFamily_Throws() =>
        ((Action)(() => CreateFor(Network.EthMain, fees: null, gas: null))).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_BtcWithGasPricesAndNoFees_Throws() =>
        ((Action)(() => CreateFor(Network.BtcMain, fees: null, new GasPrices(1, 1, 1, null, null, null, null))))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Create_BtcWithNeitherFamily_Throws() =>
        ((Action)(() => CreateFor(Network.BtcMain, fees: null, gas: null))).Should().Throw<ArgumentException>();

    private static BlockchainSnapshot CreateFor(Network network, FeeRates? fees, GasPrices? gas) =>
        BlockchainSnapshot.Create(
            network, "n", 1, "h", T, null, null, null, 0, 0,
            fees, gas, null, null, "{}", T);
}
