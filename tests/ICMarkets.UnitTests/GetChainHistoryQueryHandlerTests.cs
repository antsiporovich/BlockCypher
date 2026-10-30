using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using NSubstitute;

namespace ICMarkets.UnitTests.Features.History;

public sealed class GetChainHistoryQueryHandlerTests
{
    private readonly IBlockchainRepository _repo = Substitute.For<IBlockchainRepository>();
    private readonly SnapshotMapper _mapper = new();

    [Fact]
    public async Task Handle_ReturnsItemsInRepositoryOrder_AndNoCursorWhenNoMorePages()
    {
        // repo is the thing that actually orders by CreatedAt DESC (see
        // BlockchainRepositoryOrderingTests/BlockchainRepositoryIntegrationTests) -
        // this handler-level test only guards that it doesn't re-order or drop rows.
        // Two rows for take=10 => no probe row => no next cursor.
        var newest = CreateSnapshot(Network.BtcMain, DateTimeOffset.UtcNow);
        var oldest = CreateSnapshot(Network.BtcMain, DateTimeOffset.UtcNow.AddMinutes(-5));
        _repo.GetHistoryAsync(Network.BtcMain, 10, null, Arg.Any<CancellationToken>())
            .Returns([newest, oldest]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        var result = await handler.Handle(new GetChainHistoryQuery("btc-main", Take: 10), CancellationToken.None);

        await _repo.Received(1).GetHistoryAsync(Network.BtcMain, 10, null, Arg.Any<CancellationToken>());
        result.Items.Select(d => d.CreatedAt).Should().Equal(newest.CreatedAt, oldest.CreatedAt);
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TrimsTheProbeRow_AndEmitsANextCursor_WhenMorePagesExist()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = CreateSnapshot(Network.BtcMain, t.AddMinutes(3));
        var second = CreateSnapshot(Network.BtcMain, t.AddMinutes(2));
        var probe = CreateSnapshot(Network.BtcMain, t.AddMinutes(1));
        // take + 1 rows => there is a next page; the probe must be trimmed off the result.
        _repo.GetHistoryAsync(Network.BtcMain, 2, null, Arg.Any<CancellationToken>())
            .Returns([first, second, probe]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        var result = await handler.Handle(new GetChainHistoryQuery("btc-main", Take: 2), CancellationToken.None);

        result.Items.Select(d => d.CreatedAt).Should().Equal(first.CreatedAt, second.CreatedAt);
        result.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DecodesTheCursor_AndPassesItToTheRepository()
    {
        var cursor = new HistoryCursor(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 42);
        _repo.GetHistoryAsync(Network.BtcMain, Arg.Any<int>(), Arg.Any<HistoryCursor?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        await handler.Handle(new GetChainHistoryQuery("btc-main", Cursor: cursor.Encode()), CancellationToken.None);

        await _repo.Received(1).GetHistoryAsync(Network.BtcMain, Arg.Any<int>(), cursor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsNetworkEnumToItsStringKey()
    {
        var snapshot = CreateSnapshot(Network.EthMain, DateTimeOffset.UtcNow);
        _repo.GetHistoryAsync(Network.EthMain, Arg.Any<int>(), Arg.Any<HistoryCursor?>(), Arg.Any<CancellationToken>())
            .Returns([snapshot]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        var result = await handler.Handle(new GetChainHistoryQuery("eth-main"), CancellationToken.None);

        result.Items.Single().Network.Should().Be("eth-main");
    }

    private static BlockchainSnapshot CreateSnapshot(Network network, DateTimeOffset createdAt) =>
        BlockchainSnapshot.Create(
            network: network,
            name: network.ToString(),
            height: 1,
            hash: "hash",
            time: createdAt,
            latestUrl: null,
            previousHash: null,
            previousUrl: null,
            peerCount: 0,
            unconfirmedCount: 0,
            feeRates: null,
            gasPrices: null,
            lastForkHeight: null,
            lastForkHash: null,
            rawJson: "{}",
            createdAt: createdAt);
}
