using ICMarkets.Domain;
using ICMarkets.Domain.Entities;

namespace ICMarkets.Application.Abstractions;

public interface IBlockchainRepository : IRepository<BlockchainSnapshot>
{
    Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, int skip, CancellationToken ct);
    Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct);
}
