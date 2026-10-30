using System.Collections.Concurrent;
using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ICMarkets.UnitTests.Features.Ingestion;

// The handler takes its collaborators directly: the chain-info port and the unit-of-work
// factory port. The factory is a hand-written fake (not a mock chain) so the tests can see
// what was staged, what was committed, and how many units of work were created/disposed.
public sealed class IngestAllChainsCommandHandlerTests
{
    private static IngestAllChainsCommandHandler Handler(
        IChainInfoSource source, FakeUnitOfWorkFactory factory, TimeProvider? clock = null) =>
        new(source, factory, new SnapshotMapper(), clock ?? TimeProvider.System,
            NullLogger<IngestAllChainsCommandHandler>.Instance);

    private static IChainInfoSource HealthySource(Func<Network, string>? rawJson = null)
    {
        var source = Substitute.For<IChainInfoSource>();
        foreach (var network in Enum.GetValues<Network>())
        {
            source.GetChainInfoAsync(network, Arg.Any<CancellationToken>())
                .Returns(new ChainInfoResult(TestChainInfo(network), rawJson?.Invoke(network) ?? "{}"));
        }
        return source;
    }

    [Fact]
    public async Task Handle_OneNetworkFetchFails_StillPersistsTheOtherFour()
    {
        var source = HealthySource();
        source.GetChainInfoAsync(Network.EthMain, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChainInfoResult>(new HttpRequestException("upstream unavailable")));
        var factory = new FakeUnitOfWorkFactory();

        var result = await Handler(source, factory).Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == "eth-main");
        result.Succeeded.Select(s => s.Network).Should().NotContain("eth-main");
        factory.Saved.Should().HaveCount(4);
    }

    [Fact]
    public async Task Handle_OnePersistFails_StillReportsTheOtherFourAsSucceeded_EachInItsOwnUnitOfWork()
    {
        var factory = new FakeUnitOfWorkFactory(failOn: Network.LtcMain);

        var result = await Handler(HealthySource(), factory).Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == "ltc-main");
        factory.Saved.Should().HaveCount(4);
        // one fresh unit of work per item, and every one - including the failed - disposed.
        factory.Created.Should().Be(5);
        factory.Disposed.Should().Be(5);
    }

    [Fact]
    public async Task Handle_AllNetworksSucceed_ReturnsAllFiveWithNoFailures()
    {
        var factory = new FakeUnitOfWorkFactory();

        var result = await Handler(HealthySource(), factory).Handle(new IngestAllChainsCommand(), CancellationToken.None);

        result.Succeeded.Should().HaveCount(Enum.GetValues<Network>().Length);
        result.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CreatedAt_IsTheRequestTimeFromTheClock()
    {
        var fetchTime = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var factory = new FakeUnitOfWorkFactory();

        var result = await Handler(HealthySource(), factory, new FakeTimeProvider(fetchTime))
            .Handle(new IngestAllChainsCommand(), CancellationToken.None);

        factory.Saved.Should().OnlyContain(s => s.CreatedAt == fetchTime);
        result.Succeeded.Should().OnlyContain(s => s.CreatedAt == fetchTime);
    }

    [Fact]
    public async Task Handle_CreatedAt_IsStampedBeforeTheCall_NotWhenTheResponseArrives()
    {
        // the upstream call "takes" 7 seconds (the fake clock moves while it runs):
        // CreatedAt must still be the moment the request was issued.
        var requestTime = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var clock = new FakeTimeProvider(requestTime);
        var source = Substitute.For<IChainInfoSource>();
        foreach (var network in Enum.GetValues<Network>())
        {
            source.GetChainInfoAsync(network, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                clock.Advance(TimeSpan.FromSeconds(7));
                return new ChainInfoResult(TestChainInfo(network), "{}");
            });
        }
        var factory = new FakeUnitOfWorkFactory();

        await Handler(source, factory, clock).Handle(new IngestAllChainsCommand(), CancellationToken.None);

        // branches run in parallel, so a later branch may be stamped after an earlier one's
        // response moved the clock. But the first stamp is always taken before any response:
        // if CreatedAt were read after the call, even the earliest would be >= +7s.
        factory.Saved.Should().HaveCount(Enum.GetValues<Network>().Length);
        factory.Saved.Min(s => s.CreatedAt).Should().Be(requestTime);
    }

    [Fact]
    public async Task Handle_PersistsFeeRatesForBitcoinLikeAndGasPricesForEthereum_AndKeepsTheRawPayload()
    {
        var factory = new FakeUnitOfWorkFactory();

        await Handler(HealthySource(n => $"{{\"raw\":\"{n}\"}}"), factory)
            .Handle(new IngestAllChainsCommand(), CancellationToken.None);

        var btc = factory.Saved.Single(s => s.Network == Network.BtcMain);
        btc.FeeRates.Should().Be(new FeeRates(10, 5, 1));
        btc.GasPrices.Should().BeNull();
        btc.RawJson.Should().Be("{\"raw\":\"BtcMain\"}");

        var eth = factory.Saved.Single(s => s.Network == Network.EthMain);
        eth.GasPrices.Should().Be(new GasPrices(30, 20, 10, null, null, null, null));
        eth.FeeRates.Should().BeNull();
    }

    private static ChainInfo TestChainInfo(Network network) =>
        new(network.ToString(), 1, "hash", DateTimeOffset.UtcNow, null, null, null, 0, 0,
            FeeRates: network == Network.EthMain ? null : new FeeRates(10, 5, 1),
            GasPrices: network == Network.EthMain ? new GasPrices(30, 20, 10, null, null, null, null) : null,
            LastForkHeight: null, LastForkHash: null);

    private sealed class FakeUnitOfWorkFactory(Network? failOn = null) : IUnitOfWorkFactory
    {
        private int _created;
        private int _disposed;

        public ConcurrentBag<BlockchainSnapshot> Saved { get; } = [];
        public int Created => _created;
        public int Disposed => _disposed;

        public IIngestionUnitOfWork Create()
        {
            Interlocked.Increment(ref _created);
            return new Uow(this, failOn);
        }

        private sealed class Uow(FakeUnitOfWorkFactory owner, Network? failOn) : IIngestionUnitOfWork
        {
            private readonly Repo _repo = new();

            public IBlockchainRepository Snapshots => _repo;

            public Task<int> SaveChangesAsync(CancellationToken ct)
            {
                if (failOn is { } network && _repo.Pending.Any(s => s.Network == network))
                    return Task.FromException<int>(new InvalidOperationException("db write failed"));

                foreach (var snapshot in _repo.Pending)
                    owner.Saved.Add(snapshot);
                return Task.FromResult(_repo.Pending.Count);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposed);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class Repo : IBlockchainRepository
        {
            public List<BlockchainSnapshot> Pending { get; } = [];

            public Task AddAsync(BlockchainSnapshot entity, CancellationToken ct)
            {
                Pending.Add(entity);
                return Task.CompletedTask;
            }

            public Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, HistoryCursor? after, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
                throw new NotSupportedException();
        }
    }
}
