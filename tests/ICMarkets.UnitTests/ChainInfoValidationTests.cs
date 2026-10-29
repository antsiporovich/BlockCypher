using AwesomeAssertions;
using ICMarkets.Application.Dtos;
using ICMarkets.Domain;

namespace ICMarkets.UnitTests;

// A BlockCypher "200 OK" only proves transport. These pin down what counts as a
// usable chain summary, so a `{}` / error body / wrong-network body is a fetch
// failure instead of a stored snapshot full of defaults.
public sealed class ChainInfoValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static BlockCypherChainInfoDto ValidBtc() => new()
    {
        Name = "BTC.main",
        Height = 973576,
        Hash = "00000000000000000001dacaff55dc24",
        Time = new DateTimeOffset(2026, 5, 31, 23, 0, 0, TimeSpan.Zero),
        HighFeePerKb = 3109,
        MediumFeePerKb = 1646,
        LowFeePerKb = 1378,
    };

    private static BlockCypherChainInfoDto ValidEth() => new()
    {
        Name = "ETH.main",
        Height = 26292607,
        Hash = "74c5f4981f9832643ee276af9b00183c",
        Time = new DateTimeOffset(2026, 5, 31, 23, 0, 0, TimeSpan.Zero),
        HighGasPrice = 3314726057,
        MediumGasPrice = 2082248309,
        LowGasPrice = 850778680,
    };

    [Fact]
    public void Validate_ValidBitcoinLikeResponse_HasNoProblems() =>
        ChainInfoValidation.Validate(Network.BtcMain, ValidBtc(), Now).Should().BeEmpty();

    [Fact]
    public void Validate_ValidEthereumResponse_HasNoProblems() =>
        ChainInfoValidation.Validate(Network.EthMain, ValidEth(), Now).Should().BeEmpty();

    [Fact]
    public void Validate_EmptyObjectBody_IsRejected()
    {
        // what `{}` deserializes to: every field at its default.
        var problems = ChainInfoValidation.Validate(Network.BtcMain, new BlockCypherChainInfoDto(), Now);

        problems.Should().Contain(p => p.Contains("'name'"));
        problems.Should().Contain(p => p.Contains("'hash'"));
        problems.Should().Contain(p => p.Contains("'height'"));
        problems.Should().Contain(p => p.Contains("'time'"));
        problems.Should().Contain(p => p.Contains("fee rates"));
    }

    [Fact]
    public void Validate_MissingTime_IsRejected()
    {
        var dto = ValidBtc();
        dto.Time = default;

        ChainInfoValidation.Validate(Network.BtcMain, dto, Now).Should().Contain(p => p.Contains("'time'"));
    }

    [Fact]
    public void Validate_TimeInTheFuture_IsRejected()
    {
        var dto = ValidBtc();
        dto.Time = Now.AddDays(30);

        ChainInfoValidation.Validate(Network.BtcMain, dto, Now).Should().Contain(p => p.Contains("future"));
    }

    [Fact]
    public void Validate_NullName_IsRejected()
    {
        var dto = ValidBtc();
        dto.Name = null!;

        ChainInfoValidation.Validate(Network.BtcMain, dto, Now).Should().Contain(p => p.Contains("'name'"));
    }

    [Fact]
    public void Validate_BodyForAnotherNetwork_IsRejected_ByNameAndFamily()
    {
        // an ETH body returned for the LTC path: wrong name AND wrong fee family.
        var problems = ChainInfoValidation.Validate(Network.LtcMain, ValidEth(), Now);

        problems.Should().Contain(p => p.Contains("'name'") && p.Contains("LTC.main"));
        problems.Should().Contain(p => p.Contains("fee rates"));
        problems.Should().Contain(p => p.Contains("gas price fields"));
    }

    [Fact]
    public void Validate_BitcoinBodyForEthereum_IsRejected()
    {
        var problems = ChainInfoValidation.Validate(Network.EthMain, ValidBtc(), Now);

        problems.Should().Contain(p => p.Contains("gas prices"));
        problems.Should().Contain(p => p.Contains("fee-per-kb"));
    }

    [Fact]
    public void Validate_BothFamiliesPresent_IsRejected()
    {
        var dto = ValidBtc();
        dto.HighGasPrice = 1;
        dto.MediumGasPrice = 1;
        dto.LowGasPrice = 1;

        ChainInfoValidation.Validate(Network.BtcMain, dto, Now).Should().Contain(p => p.Contains("gas price fields"));
    }

    [Theory]
    [InlineData(Network.BtcMain, "btc.main")]
    [InlineData(Network.BtcTest3, "BTC.TEST3")]
    public void Validate_NameMatch_IsCaseInsensitive(Network network, string name)
    {
        var dto = ValidBtc();
        dto.Name = name;

        ChainInfoValidation.Validate(network, dto, Now).Should().BeEmpty();
    }

    [Fact]
    public void EnsureValid_Invalid_ThrowsWithTheNetworkAndProblems()
    {
        var act = () => ChainInfoValidation.EnsureValid(Network.DashMain, new BlockCypherChainInfoDto(), Now);

        act.Should().Throw<InvalidChainInfoException>()
            .Which.Message.Should().Contain("dash-main").And.Contain("'name'");
    }
}
