using AwesomeAssertions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ICMarkets.IntegrationTests;

// How AppDbContext stages outbox rows: one row per ingested snapshot (even when
// a save has to be retried), and none at all while the outbox is disabled.
public sealed class OutboxStagingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public OutboxStagingTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SaveChangesFailsOnce_ThenRetried_StagesExactlyOneOutboxRowPerSnapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new FailFirstSaveInterceptor())
            .Options;

        await using var db = new AppDbContext(options, new NoOpPublisher(), Options.Create(new OutboxOptions { Enabled = true }));
        await db.Database.EnsureCreatedAsync();

        db.Snapshots.Add(NewSnapshot());

        // first save fails AFTER the outbox row was staged (it stays tracked as Added)...
        var firstAttempt = () => db.SaveChangesAsync();
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();

        // ...the retry must not stage a second row for the same event.
        await db.SaveChangesAsync();

        (await db.Snapshots.CountAsync()).Should().Be(1);
        (await db.Outbox.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OutboxDisabled_SavingASnapshot_WritesNoOutboxRow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        await using var db = new AppDbContext(options, new NoOpPublisher(), Options.Create(new OutboxOptions { Enabled = false }));
        await db.Database.EnsureCreatedAsync();

        db.Snapshots.Add(NewSnapshot());
        await db.SaveChangesAsync();

        (await db.Snapshots.CountAsync()).Should().Be(1);
        (await db.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OutboxEnabled_SavingASnapshot_WritesOneOutboxRow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        await using var db = new AppDbContext(options, new NoOpPublisher(), Options.Create(new OutboxOptions { Enabled = true }));
        await db.Database.EnsureCreatedAsync();

        db.Snapshots.Add(NewSnapshot());
        await db.SaveChangesAsync();

        var row = await db.Outbox.SingleAsync();
        row.Type.Should().Be("SnapshotIngested");
        row.ProcessedAt.Should().BeNull();
    }

    private static BlockchainSnapshot NewSnapshot()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return BlockchainSnapshot.Create(
            network: Network.BtcMain, name: "BTC.main", height: 1, hash: "h", time: t,
            latestUrl: null, previousHash: null, previousUrl: null, peerCount: 0, unconfirmedCount: 0,
            feeRates: null, gasPrices: null, lastForkHeight: null, lastForkHash: null,
            rawJson: "{}", createdAt: t);
    }

    // throws once, from inside base.SaveChangesAsync - i.e. after AppDbContext has
    // already staged its outbox rows - to simulate a transient write failure.
    private sealed class FailFirstSaveInterceptor : SaveChangesInterceptor
    {
        private bool _failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("transient write failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
