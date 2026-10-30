using AwesomeAssertions;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.UnitTests.Mapping;

public sealed class SnapshotMapperTests
{
    private readonly SnapshotMapper _mapper = new();

    [Fact]
    public void ToDto_BtcSnapshot_MapsFeeRates_AndLeavesGasPricesNull()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = BlockchainSnapshot.Create(
            network: Network.BtcMain,
            name: "BTC.main",
            height: 973576,
            hash: "hash",
            time: createdAt,
            latestUrl: "https://example/latest",
            previousHash: "prev-hash",
            previousUrl: "https://example/previous",
            peerCount: 324,
            unconfirmedCount: 7749,
            feeRates: new FeeRates(High: 3109, Medium: 1646, Low: 1378),
            gasPrices: null,
            lastForkHeight: 969888,
            lastForkHash: "fork-hash",
            rawJson: "{}",
            createdAt: createdAt);

        var dto = _mapper.ToDto(snapshot);

        dto.Network.Should().Be("btc-main");
        dto.Name.Should().Be("BTC.main");
        dto.Height.Should().Be(973576);
        dto.FeeRates.Should().NotBeNull();
        dto.FeeRates!.High.Should().Be(3109);
        dto.FeeRates.Medium.Should().Be(1646);
        dto.FeeRates.Low.Should().Be(1378);
        dto.GasPrices.Should().BeNull();
        dto.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void ToDto_EthSnapshot_MapsGasPrices_AndLeavesFeeRatesNull()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = BlockchainSnapshot.Create(
            network: Network.EthMain,
            name: "ETH.main",
            height: 26292607,
            hash: "hash",
            time: createdAt,
            latestUrl: null,
            previousHash: null,
            previousUrl: null,
            peerCount: 0,
            unconfirmedCount: 20,
            feeRates: null,
            gasPrices: new GasPrices(
                High: 3314726057,
                Medium: 2082248309,
                Low: 850778680,
                BaseFee: 129147662,
                HighPriorityFee: 1313319154,
                MediumPriorityFee: 335230951,
                LowPriorityFee: 123415627),
            lastForkHeight: 26284130,
            lastForkHash: "fork-hash",
            rawJson: "{}",
            createdAt: createdAt);

        var dto = _mapper.ToDto(snapshot);

        dto.Network.Should().Be("eth-main");
        dto.FeeRates.Should().BeNull();
        dto.GasPrices.Should().NotBeNull();
        dto.GasPrices!.High.Should().Be(3314726057);
        dto.GasPrices.BaseFee.Should().Be(129147662);
        dto.GasPrices.HighPriorityFee.Should().Be(1313319154);
    }

    [Fact]
    public void ToDto_NeverExposesRawJson()
    {
        // RawJson is stored for audit only - the DTO has no such property, so
        // this test simply pins that contract (compile-time guarded too: a
        // RawJson property added to the DTO would need an explicit decision).
        var createdAt = DateTimeOffset.UtcNow;
        var snapshot = BlockchainSnapshot.Create(
            Network.LtcMain, "LTC.main", 1, "hash", createdAt,
            null, null, null, 0, 0, new FeeRates(1, 1, 1), null, null, null,
            rawJson: "{\"secret\":true}", createdAt: createdAt);

        var dto = _mapper.ToDto(snapshot);

        dto.GetType().GetProperty("RawJson").Should().BeNull();
    }
}
