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
    public async Task Handle_PassesTakeAndSkipThrough_AndPreservesRepositoryOrder()
    {
        // repo is the thing that actually orders by CreatedAt DESC (see
        // BlockchainRepositoryOrderingTests/BlockchainRepositoryIntegrationTests) -
        // this handler-level test only guards that it doesn't re-order or drop rows.
        var newest = CreateSnapshot(Network.BtcMain, DateTimeOffset.UtcNow);
        var oldest = CreateSnapshot(Network.BtcMain, DateTimeOffset.UtcNow.AddMinutes(-5));
        _repo.GetHistoryAsync(Network.BtcMain, 10, 5, Arg.Any<CancellationToken>())
            .Returns([newest, oldest]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        var result = await handler.Handle(new GetChainHistoryQuery("btc-main", Take: 10, Skip: 5), CancellationToken.None);

        await _repo.Received(1).GetHistoryAsync(Network.BtcMain, 10, 5, Arg.Any<CancellationToken>());
        result.Select(d => d.CreatedAt).Should().Equal(newest.CreatedAt, oldest.CreatedAt);
    }

    [Fact]
    public async Task Handle_MapsNetworkEnumToItsStringKey()
    {
        var snapshot = CreateSnapshot(Network.EthMain, DateTimeOffset.UtcNow);
        _repo.GetHistoryAsync(Network.EthMain, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([snapshot]);

        var handler = new GetChainHistoryQueryHandler(_repo, _mapper);
        var result = await handler.Handle(new GetChainHistoryQuery("eth-main"), CancellationToken.None);

        result.Single().Network.Should().Be("eth-main");
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
