using AwesomeAssertions;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using ICMarkets.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ICMarkets.IntegrationTests;

// The in-process notification runs after the commit. If a subscriber throws, the snapshot is
// ALREADY saved - reporting the save as failed would be a false result (the ingestion handler
// would count the network as failed, and a retry would insert a duplicate). The failure must
// only be logged. Fails if the exception escapes SaveChangesAsync again.
public sealed class PostCommitNotificationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PostCommitNotificationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ASubscriberThatThrowsAfterTheCommit_DoesNotFailTheSave_AndIsLogged()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        var logger = new ListLogger();

        await using var db = new AppDbContext(options, new ThrowingPublisher(), outboxOptions: null, logger);
        await db.Database.EnsureCreatedAsync();

        db.Snapshots.Add(NewSnapshot());
        var saved = await db.SaveChangesAsync();

        saved.Should().BeGreaterThan(0);
        (await db.Snapshots.CountAsync()).Should().Be(1, "the commit happened before the notification");
        logger.Errors.Should().ContainSingle().Which.Should().Contain("subscriber failed");
    }

    [Fact]
    public async Task ACancelledToken_StillPropagates_NotSwallowedAsASubscriberFailure()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var cts = new CancellationTokenSource();

        await using var db = new AppDbContext(options, new CancellingPublisher(cts), outboxOptions: null, new ListLogger());
        await db.Database.EnsureCreatedAsync();

        db.Snapshots.Add(NewSnapshot());
        var save = () => db.SaveChangesAsync(cts.Token);

        await save.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BlockchainSnapshot NewSnapshot()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return BlockchainSnapshot.Create(
            network: Network.BtcMain, name: "BTC.main", height: 1, hash: "h", time: t,
            latestUrl: null, previousHash: null, previousUrl: null, peerCount: 0, unconfirmedCount: 0,
            feeRates: new FeeRates(1, 1, 1), gasPrices: null, lastForkHeight: null, lastForkHash: null,
            rawJson: "{}", createdAt: t);
    }

    private sealed class ThrowingPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("subscriber blew up");

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new InvalidOperationException("subscriber blew up");
    }

    private sealed class CancellingPublisher(CancellationTokenSource cts) : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class ListLogger : ILogger<AppDbContext>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
