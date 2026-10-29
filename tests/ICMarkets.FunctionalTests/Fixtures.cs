namespace ICMarkets.FunctionalTests;

// Canned BlockCypher responses served by WireMock in ApiFactory, copied
// verbatim from docs/fixtures/*.json (Phase 0 captures) so the API is never
// called for real during functional tests.
internal static class Fixtures
{
    public const string BtcMain = """
        {
          "name": "BTC.main",
          "height": 973576,
          "hash": "00000000000000000001dacaff55dc24e332657749d5983fc64a46cd79e6d333",
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

    public const string EthMain = """
        {
          "name": "ETH.main",
          "height": 26292607,
          "hash": "74c5f4981f9832643ee276af9b00183c7df42917b7a83e1716d9c5886d714feb",
          "peer_count": 0,
          "unconfirmed_count": 20,
          "high_gas_price": 3314726057,
          "medium_gas_price": 2082248309,
          "low_gas_price": 850778680,
          "base_fee": 129147662,
          "last_fork_height": 26284130,
          "last_fork_hash": "32e1cf8aaaeef624ef00cd7f25910246b20165a7ce8f5326259cc0365ecc67b1"
        }
        """;

    public const string DashMain = """
        {
          "name": "DASH.main",
          "height": 2562925,
          "hash": "0000000000000021ce37b31385562d8ccb95cb5fb235089bf6ab019439925b56",
          "peer_count": 71,
          "unconfirmed_count": 25,
          "high_fee_per_kb": 23969,
          "medium_fee_per_kb": 17643,
          "low_fee_per_kb": 7378
        }
        """;

    public const string BtcTest3 = """
        {
          "name": "BTC.test3",
          "height": 4997970,
          "hash": "00000000008ea93063b8d2d3d6d2b50e75c42cbd1a734314ba692656d933771e",
          "peer_count": 122,
          "unconfirmed_count": 0,
          "high_fee_per_kb": 11146,
          "medium_fee_per_kb": 6347,
          "low_fee_per_kb": 2707
        }
        """;

    public const string LtcMain = """
        {
          "name": "LTC.main",
          "height": 3203281,
          "hash": "f3fcede03a3cf4b0d4de194fab66d96ae99cd3a656f9e0fbdc00830d9bac0c39",
          "peer_count": 324,
          "unconfirmed_count": 612,
          "high_fee_per_kb": 12127,
          "medium_fee_per_kb": 9725,
          "low_fee_per_kb": 7821
        }
        """;
}
