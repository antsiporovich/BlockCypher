using System.Text.Json;
using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.Events;
using ICMarkets.Infrastructure.Messaging;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, IPublisher publisher, IOptions<OutboxOptions> outboxOptions)
    : DbContext(options), IUnitOfWork
{
    public DbSet<BlockchainSnapshot> Snapshots => Set<BlockchainSnapshot>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

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

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.Property(m => m.Type).IsRequired().HasMaxLength(200);
            b.Property(m => m.Payload).IsRequired();
            // never ordered/compared in SQL (OutboxPublisherWorker orders by
            // Id), so no SQLite DateTimeOffset conversion needed here, unlike
            // BlockchainSnapshot.CreatedAt above.
            b.HasIndex(m => m.ProcessedAt);
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

        // Transactional outbox: the row is added to the SAME SaveChanges call
        // as the snapshot that raised the event, so both commit together or
        // neither does - see OutboxMessage for why that matters. Gated on
        // Outbox:Enabled - OutboxPublisherWorker is the only thing that ever
        // drains this table, and it no-ops when disabled (default for
        // local/SQLite dev and the functional tests), so writing rows nobody
        // will read would just grow the table forever. Flipping Outbox:Enabled
        // back on starts writing - and publishing - events from that point on.
        if (outboxOptions.Value.Enabled)
        {
            foreach (var domainEvent in pendingEvents)
            {
                if (domainEvent is SnapshotIngested ingested)
                {
                    Outbox.Add(OutboxMessage.Create(
                        type: nameof(SnapshotIngested),
                        payload: JsonSerializer.Serialize(ingested),
                        occurredAt: ingested.CreatedAt));
                }
            }
        }

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
