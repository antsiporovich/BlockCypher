using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.Infrastructure.Persistence;

public class BlockchainRepository(AppDbContext db) : IBlockchainRepository
{
    // No SaveChanges here - the caller commits via IUnitOfWork (see
    // IngestAllChainsCommandHandler), so a handler can stage several repository
    // calls and commit them together.
    public Task AddAsync(BlockchainSnapshot snapshot, CancellationToken ct)
    {
        db.Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    // Order by CreatedAt DESC as the task requires, with Id DESC as a deterministic
    // tiebreaker when two rows share a timestamp. CreatedAt is always UTC
    // (TimeProvider.GetUtcNow), so it sorts correctly on both SQL Server and Sqlite.
    // Served by the (Network, CreatedAt DESC) index; SQL Server appends the clustered
    // PK (Id) to the nonclustered index, so the Id tiebreaker is covered too.
    // AsNoTracking: these are read-only query paths, no change tracking needed.
    // Known optimization (not done): these queries materialize the whole entity,
    // including the large RawJson column, which the mapper then discards. A
    // server-side Select into the DTO would skip it; negligible for rows this
    // small, and it would move DTO shaping into the repository, so it is left
    // as a documented trade-off (see README, Known limitations).
    public async Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, int skip, CancellationToken ct) =>
        await db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
        await db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
}
