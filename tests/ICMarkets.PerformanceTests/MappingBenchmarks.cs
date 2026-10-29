using BenchmarkDotNet.Attributes;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain.Entities;

namespace ICMarkets.PerformanceTests;

[MemoryDiagnoser]
public class MappingBenchmarks
{
    private readonly SnapshotMapper _mapper = new();
    private BlockchainSnapshot _one = null!;
    private List<BlockchainSnapshot> _fifty = null!;

    [GlobalSetup]
    public void Setup()
    {
        _one = SnapshotFixtures.OneBtcSnapshot();
        _fifty = SnapshotFixtures.Snapshots(50);
    }

    [Benchmark(Baseline = true)]
    public object MapOne() => _mapper.ToDto(_one);

    // mirrors mapping a full default-size (`take=50`) history page.
    [Benchmark]
    public List<object> MapFifty() => _fifty.Select(s => (object)_mapper.ToDto(s)).ToList();
}
