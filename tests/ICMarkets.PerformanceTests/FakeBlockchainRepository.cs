using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;

namespace ICMarkets.PerformanceTests;

// In-memory stand-in for IBlockchainRepository - the query-handler benchmark
// measures GetChainHistoryQueryHandler's own overhead (parsing, mapping,
// orchestration), not SQL Server round-trip time, which is Testcontainers
// integration-test territory, not a micro-benchmark's job.
internal sealed class FakeBlockchainRepository(List<BlockchainSnapshot> seed) : IBlockchainRepository
{
    public Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, int skip, CancellationToken ct) =>
        Task.FromResult(seed.Skip(skip).Take(take).ToList());

    public Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
        Task.FromResult(seed.FirstOrDefault());

    public Task AddAsync(BlockchainSnapshot entity, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by the query-path benchmark.");
}
