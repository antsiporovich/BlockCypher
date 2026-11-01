using System.Text.Json;
using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.Events;
using ICMarkets.Infrastructure.Messaging;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.Persistence;

// outboxOptions is optional so design-time (DesignTimeDbContextFactory) and the
// integration tests can construct the context with `new AppDbContext(options,
// publisher)` without wiring options; at runtime DI always injects the real
// IOptions<OutboxOptions>. Null => outbox disabled (the write is gated below).
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IPublisher publisher,
    IOptions<OutboxOptions>? outboxOptions = null,
    ILogger<AppDbContext>? logger = null)
    : DbContext(options), IUnitOfWork
{
    // Domain events whose outbox row is already staged in this context's change
    // tracker. If a SaveChanges fails (the events are not cleared and the staged
    // row stays tracked as Added) and is then retried, those events must not
    // stage a SECOND row - otherwise the retry commits two outbox rows for one
    // snapshot. Reference equality on purpose: events are records, so value
    // equality would also collapse genuinely distinct-but-equal events.
    private readonly HashSet<object> _stagedForOutbox = new(ReferenceEqualityComparer.Instance);

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
            b.HasIndex(m => m.ProcessedAt);

            // OutboxPublisherWorker compares ProcessedAt in SQL (the retention
            // purge: ProcessedAt < cutoff), and SQLite cannot translate a
            // DateTimeOffset comparison (stored as TEXT). Same fix as
            // BlockchainSnapshot.CreatedAt above: on SQLite only, store UTC
            // ticks; SQL Server keeps its native datetimeoffset. ProcessedAt is
            // always UTC (DateTimeOffset.UtcNow), so nothing is lost. NULLs
            // (unprocessed rows) are never passed through the converter.
            if (Database.IsSqlite())
            {
                b.Property(m => m.ProcessedAt).HasConversion(
                    d => d!.Value.UtcDateTime.Ticks,
                    t => (DateTimeOffset?)new DateTimeOffset(t, TimeSpan.Zero));
            }
        });
    }

    // DbContext funnels SaveChangesAsync() into SaveChangesAsync(bool, ct), and SaveChanges()
    // into SaveChanges(bool). Overriding those two catches every entry point. Overriding only
    // SaveChangesAsync(ct) left the (bool, ct) and sync paths writing the snapshot and dropping
    // the outbox row with no error.
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(
            "Use SaveChangesAsync: AppDbContext's transactional outbox and domain-event dispatch are async-only.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var events = StageOutboxAndCollectEvents();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        ClearStagedAndEvents();
        await DispatchNotificationsAsync(events, ct);
        return result;
    }

    // Transactional outbox: the row is added to the SAME SaveChanges call as the snapshot
    // that raised the event, so both commit together or neither does. Gated on
    // Outbox:Enabled - the worker no-ops when disabled, so writing rows nobody will read
    // would grow the table forever. Deduped via _stagedForOutbox so a retried save (the
    // event is still on the entity, the staged row still tracked as Added) does not insert
    // a second row. Only SnapshotIngested exists today.
    private List<IDomainEvent> StageOutboxAndCollectEvents()
    {
        var pendingEvents = ChangeTracker.Entries<BlockchainSnapshot>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        if (outboxOptions?.Value.Enabled == true)
        {
            foreach (var domainEvent in pendingEvents)
            {
                if (domainEvent is SnapshotIngested ingested && _stagedForOutbox.Add(domainEvent))
                {
                    Outbox.Add(OutboxMessage.Create(
                        type: nameof(SnapshotIngested),
                        payload: JsonSerializer.Serialize(ingested),
                        occurredAt: ingested.CreatedAt));
                }
            }
        }

        return pendingEvents;
    }

    private void ClearStagedAndEvents()
    {
        _stagedForOutbox.Clear();
        foreach (var entry in ChangeTracker.Entries<BlockchainSnapshot>())
            entry.Entity.ClearDomainEvents();
    }

    // In-process notifications run AFTER the commit, so they are best-effort: the data is
    // already durable, and a throwing subscriber must not turn a successful save into a
    // failed one. The reliable side-effect path is the outbox. Only a real cancellation propagates.
    private async Task DispatchNotificationsAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            if (domainEvent is not SnapshotIngested ingested)
                continue;

            try
            {
                await publisher.Publish(new ICMarkets.Application.Notifications.SnapshotIngestedNotification(ingested), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger?.LogError(ex,
                    "A post-commit SnapshotIngested subscriber failed; the snapshot was saved and the failure is ignored.");
            }
        }
    }
}
