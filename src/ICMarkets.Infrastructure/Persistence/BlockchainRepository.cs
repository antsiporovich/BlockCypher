using ICMarkets.Application.Abstractions;
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

    public async Task<List<BlockchainSnapshot>> GetHistoryAsync(string network, int take, int skip, CancellationToken ct) =>
        await db.Snapshots
            .Where(s => s.Network == network)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
}
