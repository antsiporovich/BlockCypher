using AwesomeAssertions;
using ICMarkets.Domain.ValueObjects;
using ICMarkets.Infrastructure.BlockCypher;

namespace ICMarkets.UnitTests.BlockCypher;

// The adapter's translation of BlockCypher's wire contract into the neutral ChainInfo:
// the fee/gas value objects exist only when that family was actually present.
public sealed class BlockCypherChainInfoMapperTests
{
    [Fact]
    public void ToChainInfo_BtcBody_MapsFeeRates_AndLeavesGasPricesNull()
    {
        var dto = new BlockCypherChainInfoDto
        {
            Name = "BTC.main", Height = 100, Hash = "h", Time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LatestUrl = "l", PreviousHash = "p", PreviousUrl = "pu", PeerCount = 3, UnconfirmedCount = 4,
            HighFeePerKb = 10, MediumFeePerKb = 5, LowFeePerKb = 1, LastForkHeight = 7, LastForkHash = "f",
        };

        var info = BlockCypherChainInfoMapper.ToChainInfo(dto);

        info.Name.Should().Be("BTC.main");
        info.Height.Should().Be(100);
        info.Hash.Should().Be("h");
        info.Time.Should().Be(dto.Time);
        info.LatestUrl.Should().Be("l");
        info.PreviousHash.Should().Be("p");
        info.PreviousUrl.Should().Be("pu");
        info.PeerCount.Should().Be(3);
        info.UnconfirmedCount.Should().Be(4);
        info.FeeRates.Should().Be(new FeeRates(10, 5, 1));
        info.GasPrices.Should().BeNull();
        info.LastForkHeight.Should().Be(7);
        info.LastForkHash.Should().Be("f");
    }

    [Fact]
    public void ToChainInfo_EthBody_MapsGasPrices_AndLeavesFeeRatesNull()
    {
        var dto = new BlockCypherChainInfoDto
        {
            Name = "ETH.main", Height = 1, Hash = "h",
            HighGasPrice = 30, MediumGasPrice = 20, LowGasPrice = 10,
            BaseFee = 5, HighPriorityFee = 3, MediumPriorityFee = 2, LowPriorityFee = 1,
        };

        var info = BlockCypherChainInfoMapper.ToChainInfo(dto);

        info.GasPrices.Should().Be(new GasPrices(30, 20, 10, 5, 3, 2, 1));
        info.FeeRates.Should().BeNull();
    }

    [Fact]
    public void ToChainInfo_NoFeeOrGasFieldsPresent_LeavesBothNull()
    {
        // neither value object should be fabricated as all-null.
        var info = BlockCypherChainInfoMapper.ToChainInfo(new BlockCypherChainInfoDto { Name = "X", Height = 1, Hash = "h" });

        info.FeeRates.Should().BeNull();
        info.GasPrices.Should().BeNull();
    }
}
