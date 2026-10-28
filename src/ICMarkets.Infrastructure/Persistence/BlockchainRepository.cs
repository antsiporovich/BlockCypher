using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.Infrastructure.Persistence;

public class BlockchainRepository(AppDbContext db) : IBlockchainRepository
{
    // No SaveChanges here - the caller commits via IUnitOfWork (see
    // IngestChainCommandHandler), so a handler can stage several repository
    // calls and commit them together.
    public Task AddAsync(BlockchainSnapshot snapshot, CancellationToken ct)
    {
        db.Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    // shortcut: order by Id rather than CreatedAt (a DateTimeOffset) - the Sqlite
    // provider cannot translate ORDER BY over DateTimeOffset, and Id is monotonically
    // increasing with insertion order anyway, so this is equivalent and provider-agnostic.
    // AsNoTracking: these are read-only query paths, no change tracking needed.
    public async Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, int skip, CancellationToken ct) =>
        await db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
        await db.Snapshots
            .AsNoTracking()
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
}
