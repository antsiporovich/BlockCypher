using System.Text.Json;
using AwesomeAssertions;
using ICMarkets.Infrastructure.BlockCypher;

namespace ICMarkets.UnitTests.Dtos;

// Fixture bodies are verbatim copies of the Phase 0 captures in
// docs/fixtures/*.json (kept inline rather than read from disk so this test
// has no dependency on the solution's working directory at test-run time).
public sealed class BlockCypherChainInfoDtoDeserializationTests
{
    private const string BtcMainJson = """
        {
          "name": "BTC.main",
          "height": 973576,
          "hash": "00000000000000000001dacaff55dc24e332657749d5983fc64a46cd79e6d333",
          "time": "2026-10-28T16:56:28.258283481Z",
          "latest_url": "https://api.blockcypher.com/v1/btc/main/blocks/00000000000000000001dacaff55dc24e332657749d5983fc64a46cd79e6d333",
          "previous_hash": "000000000000000000007ba114b4eb571af6018acc8e8a64359d4cde52cd3ae3",
          "previous_url": "https://api.blockcypher.com/v1/btc/main/blocks/000000000000000000007ba114b4eb571af6018acc8e8a64359d4cde52cd3ae3",
          "peer_count": 324,
          "unconfirmed_count": 7749,
          "high_fee_per_kb": 3109,
          "medium_fee_per_kb": 1646,
          "low_fee_per_kb": 1378,
          "last_fork_height": 969888,
          "last_fork_hash": "000000000000000000009c4117126f6f55cf27e4b4da28ac106b12130a442231"
        }
        """;

    private const string EthMainJson = """
        {
          "name": "ETH.main",
          "height": 26292607,
          "hash": "74c5f4981f9832643ee276af9b00183c7df42917b7a83e1716d9c5886d714feb",
          "time": "2026-10-28T17:04:38.477732482Z",
          "latest_url": "https://api.blockcypher.com/v1/eth/main/blocks/74c5f4981f9832643ee276af9b00183c7df42917b7a83e1716d9c5886d714feb",
          "previous_hash": "00a9799133acc4999a8d29b6c325151fc07802ed67ae502615e98efb17f99009",
          "previous_url": "https://api.blockcypher.com/v1/eth/main/blocks/00a9799133acc4999a8d29b6c325151fc07802ed67ae502615e98efb17f99009",
          "peer_count": 0,
          "unconfirmed_count": 20,
          "high_gas_price": 3314726057,
          "medium_gas_price": 2082248309,
          "low_gas_price": 850778680,
          "high_priority_fee": 1313319154,
          "medium_priority_fee": 335230951,
          "low_priority_fee": 123415627,
          "base_fee": 129147662,
          "last_fork_height": 26284130,
          "last_fork_hash": "32e1cf8aaaeef624ef00cd7f25910246b20165a7ce8f5326259cc0365ecc67b1"
        }
        """;

    [Fact]
    public void Deserialize_BtcMainFixture_MapsFeeFields_AndLeavesGasFieldsNull()
    {
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(BtcMainJson);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("BTC.main");
        dto.Height.Should().Be(973576);
        dto.PeerCount.Should().Be(324);
        dto.UnconfirmedCount.Should().Be(7749);
        dto.HighFeePerKb.Should().Be(3109);
        dto.MediumFeePerKb.Should().Be(1646);
        dto.LowFeePerKb.Should().Be(1378);
        dto.LastForkHeight.Should().Be(969888);
        dto.HighGasPrice.Should().BeNull();
        dto.BaseFee.Should().BeNull();
    }

    [Fact]
    public void Deserialize_EthMainFixture_MapsGasAndEip1559Fields_AndLeavesFeeFieldsNull()
    {
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(EthMainJson);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("ETH.main");
        dto.Height.Should().Be(26292607);
        // high_gas_price (3314726057) exceeds Int32.MaxValue - the long-typed
        // DTO field is exactly why, per the GasPrices value-object comment.
        dto.HighGasPrice.Should().Be(3314726057);
        dto.MediumGasPrice.Should().Be(2082248309);
        dto.LowGasPrice.Should().Be(850778680);
        dto.BaseFee.Should().Be(129147662);
        dto.HighPriorityFee.Should().Be(1313319154);
        dto.MediumPriorityFee.Should().Be(335230951);
        dto.LowPriorityFee.Should().Be(123415627);
        dto.HighFeePerKb.Should().BeNull();
    }

    [Fact]
    public void Deserialize_UsesSnakeCasePropertyNames()
    {
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(BtcMainJson);

        dto!.LatestUrl.Should().StartWith("https://api.blockcypher.com/v1/btc/main/blocks/");
        dto.PreviousHash.Should().NotBeNullOrEmpty();
        dto.LastForkHash.Should().NotBeNullOrEmpty();
    }
}
