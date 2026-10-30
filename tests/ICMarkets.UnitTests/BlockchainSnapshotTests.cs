using AwesomeAssertions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;

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
            null, null, null, null, rawJson, T);

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
}
