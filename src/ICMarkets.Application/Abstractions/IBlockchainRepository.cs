using ICMarkets.Application.Features.History;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;

namespace ICMarkets.Application.Abstractions;

public interface IBlockchainRepository : IRepository<BlockchainSnapshot>
{
    /// <summary>
    /// Keyset page over <c>(CreatedAt DESC, Id DESC)</c>: rows strictly after <paramref name="after"/>
    /// (or the newest rows when it is null). Returns up to <c>take + 1</c> rows - the extra row is a
    /// probe that lets the caller tell whether a next page exists.
    /// </summary>
    Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, HistoryCursor? after, CancellationToken ct);
    Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct);
}
