using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, IPublisher publisher)
    : DbContext(options), IUnitOfWork
{
    public DbSet<BlockchainSnapshot> Snapshots => Set<BlockchainSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlockchainSnapshot>(b =>
        {
            b.Ignore(s => s.DomainEvents);

            b.Property(s => s.Network)
                .HasConversion(n => NetworkKey.ToKey(n), s => NetworkKey.Parse(s));

            // supports GetHistoryAsync/GetLatestAsync: filter by Network, newest first.
            b.HasIndex(s => new { s.Network, s.CreatedAt })
                .IsDescending(false, true);

            // SQLite can't ORDER BY / compare DateTimeOffset in SQL (it's stored as
            // TEXT), which the CreatedAt-DESC history query needs. On SQLite only,
            // store CreatedAt as UTC ticks (BIGINT) so it sorts natively and uses the
            // index above; SQL Server keeps its native datetimeoffset. CreatedAt is
            // always UTC (TimeProvider.GetUtcNow), so the conversion loses nothing.
            if (Database.IsSqlite())
            {
                b.Property(s => s.CreatedAt).HasConversion(
                    d => d.UtcDateTime.Ticks,
                    t => new DateTimeOffset(t, TimeSpan.Zero));
            }

            b.OwnsOne(s => s.FeeRates, fr =>
            {
                fr.Property(f => f.High).HasColumnName("HighFeePerKb");
                fr.Property(f => f.Medium).HasColumnName("MediumFeePerKb");
                fr.Property(f => f.Low).HasColumnName("LowFeePerKb");
            });

            b.OwnsOne(s => s.GasPrices, gp =>
            {
                gp.Property(g => g.High).HasColumnName("HighGasPrice");
                gp.Property(g => g.Medium).HasColumnName("MediumGasPrice");
                gp.Property(g => g.Low).HasColumnName("LowGasPrice");
                gp.Property(g => g.BaseFee).HasColumnName("BaseFee");
                gp.Property(g => g.HighPriorityFee).HasColumnName("HighPriorityFee");
                gp.Property(g => g.MediumPriorityFee).HasColumnName("MediumPriorityFee");
                gp.Property(g => g.LowPriorityFee).HasColumnName("LowPriorityFee");
            });
        });
    }

    // dispatches domain events raised on tracked entities after a successful
    // save (only one event type exists today - SnapshotIngested - so this
    // stays a direct check rather than a speculative generic dispatcher).
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var pendingEvents = ChangeTracker.Entries<BlockchainSnapshot>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        var result = await base.SaveChangesAsync(ct);

        foreach (var entry in ChangeTracker.Entries<BlockchainSnapshot>())
            entry.Entity.ClearDomainEvents();

        foreach (var domainEvent in pendingEvents)
        {
            if (domainEvent is SnapshotIngested ingested)
                await publisher.Publish(new ICMarkets.Application.Notifications.SnapshotIngestedNotification(ingested), ct);
        }

        return result;
    }
}
