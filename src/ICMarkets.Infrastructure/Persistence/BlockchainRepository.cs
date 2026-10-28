using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.Infrastructure.Persistence;

public class BlockchainRepository(AppDbContext db) : IBlockchainRepository
{
    public async Task AddAsync(BlockchainSnapshot snapshot, CancellationToken ct)
    {
        db.Snapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
    }

    // shortcut: order by Id rather than CreatedAt (a DateTimeOffset) - the Sqlite
    // provider cannot translate ORDER BY over DateTimeOffset, and Id is monotonically
    // increasing with insertion order anyway, so this is equivalent and provider-agnostic.
    public async Task<List<BlockchainSnapshot>> GetHistoryAsync(Network network, int take, int skip, CancellationToken ct) =>
        await db.Snapshots
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<BlockchainSnapshot?> GetLatestAsync(Network network, CancellationToken ct) =>
        await db.Snapshots
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
}
