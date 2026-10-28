using ICMarkets.Domain.Entities;

namespace ICMarkets.Application.Abstractions;

// ponytail: just the two methods Phase 2 needs. Phase 5 adds pagination hardening,
// AsNoTracking and the (Network, CreatedAt DESC) index; UnitOfWork arrives when a
// multi-network ingestion run (Phase 5) needs one atomic commit across repositories.
public interface IBlockchainRepository
{
    Task AddAsync(BlockchainSnapshot snapshot, CancellationToken ct);
    Task<List<BlockchainSnapshot>> GetHistoryAsync(string network, int take, int skip, CancellationToken ct);
}
