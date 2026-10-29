using BenchmarkDotNet.Attributes;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Mapping;

namespace ICMarkets.PerformanceTests;

[MemoryDiagnoser]
public class QueryHandlerBenchmarks
{
    private GetChainHistoryQueryHandler _handler = null!;
    private GetChainHistoryQuery _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        var repo = new FakeBlockchainRepository(SnapshotFixtures.Snapshots(50));
        _handler = new GetChainHistoryQueryHandler(repo, new SnapshotMapper());
        _query = new GetChainHistoryQuery("btc-main", Take: 50, Skip: 0);
    }

    [Benchmark]
    public Task<List<BlockchainSnapshotDto>> HandleHistoryQuery() => _handler.Handle(_query, CancellationToken.None);
}
