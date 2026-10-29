using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ICMarkets.UnitTests.Features.Ingestion;

// IngestAllChainsCommandHandler resolves IBlockCypherClient (producer side)
// and ISender (consumer side) from a fresh DI scope per Parallel.ForEachAsync
// branch and per consumer loop (see the handler's own comments), rather than
// taking them as constructor dependencies - so the cleanest substitution
// point is a tiny real ServiceCollection with NSubstitute doubles behind it,
// not a hand-mocked IServiceScopeFactory/IServiceScope chain.
public sealed class IngestAllChainsCommandHandlerTests
{
    [Fact]
    public async Task Handle_OneNetworkFetchFails_StillPersistsTheOtherFour()
    {
        var client = Substitute.For<IBlockCypherClient>();
        var sender = Substitute.For<ISender>();

        client.GetChainInfoAsync(Network.EthMain, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChainInfoResult>(new HttpRequestException("upstream unavailable")));
        foreach (var network in Enum.GetValues<Network>().Where(n => n != Network.EthMain))
        {
            client.GetChainInfoAsync(network, Arg.Any<CancellationToken>())
                .Returns(new ChainInfoResult(new BlockCypherChainInfoDto { Name = network.ToString() }, "{}"));
        }

        sender.Send(Arg.Any<PersistSnapshotCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = (PersistSnapshotCommand)callInfo[0];
                return Task.FromResult(new BlockchainSnapshotDto(
                    1, NetworkKey.ToKey(cmd.Network), cmd.Result.Dto.Name, 1, "hash",
                    DateTimeOffset.UtcNow, null, null, null, 0, 0, null, null, null, null, DateTimeOffset.UtcNow));
            });

        var scopeFactory = BuildScopeFactory(client, sender);
        var handler = new IngestAllChainsCommandHandler(scopeFactory, NullLogger<IngestAllChainsCommandHandler>.Instance);

        var result = await handler.Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == Network.EthMain);
        result.Succeeded.Select(s => s.Network).Should().NotContain("eth-main");
    }

    [Fact]
    public async Task Handle_OnePersistFails_StillReportsTheOtherFourAsSucceeded()
    {
        var client = Substitute.For<IBlockCypherClient>();
        var sender = Substitute.For<ISender>();

        foreach (var network in Enum.GetValues<Network>())
        {
            client.GetChainInfoAsync(network, Arg.Any<CancellationToken>())
                .Returns(new ChainInfoResult(new BlockCypherChainInfoDto { Name = network.ToString() }, "{}"));
        }

        sender.Send(Arg.Any<PersistSnapshotCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = (PersistSnapshotCommand)callInfo[0];
                if (cmd.Network == Network.LtcMain)
                    return Task.FromException<BlockchainSnapshotDto>(new InvalidOperationException("db write failed"));

                return Task.FromResult(new BlockchainSnapshotDto(
                    1, NetworkKey.ToKey(cmd.Network), cmd.Result.Dto.Name, 1, "hash",
                    DateTimeOffset.UtcNow, null, null, null, 0, 0, null, null, null, null, DateTimeOffset.UtcNow));
            });

        var scopeFactory = BuildScopeFactory(client, sender);
        var handler = new IngestAllChainsCommandHandler(scopeFactory, NullLogger<IngestAllChainsCommandHandler>.Instance);

        var result = await handler.Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == Network.LtcMain);
    }

    [Fact]
    public async Task Handle_AllNetworksSucceed_ReturnsAllFiveWithNoFailures()
    {
        var client = Substitute.For<IBlockCypherClient>();
        var sender = Substitute.For<ISender>();

        foreach (var network in Enum.GetValues<Network>())
        {
            client.GetChainInfoAsync(network, Arg.Any<CancellationToken>())
                .Returns(new ChainInfoResult(new BlockCypherChainInfoDto { Name = network.ToString() }, "{}"));
        }

        sender.Send(Arg.Any<PersistSnapshotCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = (PersistSnapshotCommand)callInfo[0];
                return Task.FromResult(new BlockchainSnapshotDto(
                    1, NetworkKey.ToKey(cmd.Network), cmd.Result.Dto.Name, 1, "hash",
                    DateTimeOffset.UtcNow, null, null, null, 0, 0, null, null, null, null, DateTimeOffset.UtcNow));
            });

        var scopeFactory = BuildScopeFactory(client, sender);
        var handler = new IngestAllChainsCommandHandler(scopeFactory, NullLogger<IngestAllChainsCommandHandler>.Instance);

        var result = await handler.Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(Enum.GetValues<Network>().Length);
        result.Failed.Should().BeEmpty();
    }

    private static IServiceScopeFactory BuildScopeFactory(IBlockCypherClient client, ISender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => client);
        services.AddScoped(_ => sender);
        return services.BuildServiceProvider(validateScopes: true).GetRequiredService<IServiceScopeFactory>();
    }
}
