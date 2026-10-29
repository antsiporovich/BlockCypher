using System.Collections.Concurrent;
using AwesomeAssertions;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Messaging;
using ICMarkets.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ICMarkets.IntegrationTests;

// Delivery guarantees of the outbox drain, against a real AppDbContext on
// in-memory SQLite and a scripted IEventPublisher: a row is marked processed
// only after the publisher call COMPLETES (which, for the real RabbitMQ
// publisher, means the broker confirmed it), and any failure leaves it
// unprocessed so the next tick retries it.
//
// The retention purge (ProcessedAt < cutoff, ExecuteDeleteAsync) runs for real
// here too: AppDbContext stores ProcessedAt as UTC ticks on SQLite so the
// comparison is translatable.
public sealed class OutboxPublisherWorkerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;
    private OutboxPublisherWorker? _worker;

    public Task InitializeAsync()
    {
        _connection.Open();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
            await _worker.StopAsync(CancellationToken.None);
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PublishFails_RowStaysUnprocessed_ThenIsRetriedWithTheSameMessageId()
    {
        var publisher = new ScriptedPublisher(failOnAttempt: n => n == 1);
        await StartAsync(publisher, rows: 1);

        await WaitUntilAsync(async () => (await ProcessedIdsAsync()).Count == 1);

        publisher.Attempts.Should().Be(2, "the first attempt failed and the row had to be retried");
        publisher.MessageIds.Should().OnlyContain(id => id == "1", "the id is the outbox row id, stable across retries");
    }

    [Fact]
    public async Task BrokerDownForGood_RowNeverGetsMarkedProcessed_AndKeepsBeingRetried()
    {
        var publisher = new ScriptedPublisher(failOnAttempt: _ => true);
        await StartAsync(publisher, rows: 1);

        await WaitUntilAsync(() => Task.FromResult(publisher.Attempts >= 2));

        (await ProcessedIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FailureMidBatch_DoesNotRepublishRowsTheBrokerAlreadyConfirmed()
    {
        // 3 rows, the 2nd publish call fails once. Row 1 was confirmed and must be
        // saved immediately - not published again just because row 2 failed.
        var publisher = new ScriptedPublisher(failOnAttempt: n => n == 2);
        await StartAsync(publisher, rows: 3);

        await WaitUntilAsync(async () => (await ProcessedIdsAsync()).Count == 3);

        publisher.MessageIds.Count(id => id == "1").Should().Be(1, "row 1 was confirmed before row 2 failed");
        publisher.MessageIds.Count(id => id == "2").Should().Be(2, "row 2 failed once and was retried");
        publisher.MessageIds.Count(id => id == "3").Should().Be(1);
    }

    [Fact]
    public async Task Retention_PurgesOldProcessedRows_AndKeepsRecentAndUnprocessedOnes()
    {
        var publisher = new ScriptedPublisher(failOnAttempt: _ => true); // nothing new gets published
        await StartAsync(publisher, rows: 0, seed: db =>
        {
            var old = OutboxMessage.Create("SnapshotIngested", "{}", DateTimeOffset.UtcNow.AddDays(-3));
            old.MarkProcessed(DateTimeOffset.UtcNow.AddHours(-48));   // past the 24h default
            var recent = OutboxMessage.Create("SnapshotIngested", "{}", DateTimeOffset.UtcNow.AddHours(-2));
            recent.MarkProcessed(DateTimeOffset.UtcNow.AddHours(-1)); // inside the window
            var pending = OutboxMessage.Create("SnapshotIngested", "{}", DateTimeOffset.UtcNow.AddDays(-3));
            db.Outbox.AddRange(old, recent, pending);
        });

        await WaitUntilAsync(async () => (await AllIdsAsync()).Count == 2);

        // ids: 1 = old processed (purged), 2 = recent processed, 3 = unprocessed
        (await AllIdsAsync()).Should().BeEquivalentTo(new long[] { 2, 3 });
    }

    private async Task StartAsync(ScriptedPublisher publisher, int rows, Action<AppDbContext>? seed = null)
    {
        var options = Options.Create(new OutboxOptions { Enabled = true, PollIntervalSeconds = 1, BatchSize = 10 });

        var services = new ServiceCollection();
        services.AddSingleton<IPublisher>(new NoOpPublisher());
        services.AddSingleton(options);
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            for (var i = 0; i < rows; i++)
                db.Outbox.Add(OutboxMessage.Create("SnapshotIngested", "{}", DateTimeOffset.UtcNow));
            seed?.Invoke(db);
            await db.SaveChangesAsync();
        }

        _worker = new OutboxPublisherWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<OutboxPublisherWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);
    }

    private async Task<List<long>> AllIdsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Outbox.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();
    }

    private async Task<List<long>> ProcessedIdsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Outbox.Where(m => m.ProcessedAt != null).Select(m => m.Id).ToListAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition not met within {Timeout}.");
    }

    private sealed class ScriptedPublisher(Func<int, bool> failOnAttempt) : IEventPublisher
    {
        private readonly ConcurrentQueue<string> _ids = new();
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);
        public IReadOnlyCollection<string> MessageIds => _ids.ToArray();

        public Task PublishAsync(string messageId, string type, string payload, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            _ids.Enqueue(messageId);

            return failOnAttempt(attempt)
                ? Task.FromException(new InvalidOperationException("broker unavailable"))
                : Task.CompletedTask;
        }
    }
}
