using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Features.History;
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

    // Keyset (cursor) pagination over (CreatedAt DESC, Id DESC) - the task requires CreatedAt DESC,
    // with Id DESC as a deterministic tiebreaker when two rows share a timestamp. Unlike OFFSET
    // (.Skip), a cursor selects rows strictly "after" the previous page's last row, so a row
    // inserted between two page requests (the background worker does this routinely) can neither
    // shift a page nor duplicate a row across pages. Fetch take + 1 so the caller can tell whether
    // a next page exists without a second COUNT query.
    // CreatedAt is always UTC (TimeProvider.GetUtcNow). On SQLite it is stored as BIGINT ticks (see
    // AppDbContext), so both the ORDER BY and the "<" / "==" keyset comparisons translate there as
    // well as on SQL Server. Served by the (Network, CreatedAt DESC) index; SQL Server appends the
    // clustered PK (Id) to the nonclustered index, so the Id tiebreaker is covered too.
    // AsNoTracking: these are read-only query paths, no change tracking needed.
    // Known optimization (not done): these queries materialize the whole entity, including the large
    // RawJson column, which the mapper then discards. A server-side Select into the DTO would skip
    // it; negligible for rows this small, and it would move DTO shaping into the repository, so it is
    // left as a documented trade-off (see README, Known limitations).
    public async Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, HistoryCursor? after, CancellationToken ct)
    {
        var query = db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network);

        if (after is { } cursor)
            query = query.Where(s => s.CreatedAt < cursor.CreatedAt
                                     || (s.CreatedAt == cursor.CreatedAt && s.Id < cursor.Id));

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Take(take + 1)
            .ToListAsync(ct);
    }

    public async Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
        await db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
}
