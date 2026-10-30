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

// RabbitMQ.Client awaits a publisher confirm with no timeout of its own, so a broker that
// keeps the connection open but stops confirming makes PublishAsync wait indefinitely - and
// the worker publishes rows one after another, so the whole queue stalls behind it while the
// service still looks alive. The per-publish budget must end such an attempt and let the
// next tick try again. Fails without the budget: the first attempt never returns, so the
// attempt counter never reaches 2.
public sealed class OutboxPublishTimeoutTests
{
    [Fact]
    public async Task HungPublisher_AttemptIsEndedByTheBudget_RowStaysUnprocessed_NextTickRetries()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var attempts = 0;
        var options = Options.Create(new OutboxOptions
        {
            Enabled = true, PollIntervalSeconds = 1, PublishTimeoutSeconds = 1, BatchSize = 10,
        });
        var services = new ServiceCollection();
        services.AddSingleton<IPublisher>(new NoOpPublisher());
        services.AddSingleton(options);
        services.AddSingleton<IEventPublisher>(new HungPublisher(() => Interlocked.Increment(ref attempts)));
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Outbox.Add(OutboxMessage.Create("SnapshotIngested", "{}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var worker = new OutboxPublisherWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<OutboxPublisherWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2, TimeSpan.FromSeconds(20));

        worker.ExecuteTask!.IsCompleted.Should().BeFalse("a hung publish must not stop the worker");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Outbox.CountAsync(m => m.ProcessedAt == null)).Should().Be(1, "nothing was confirmed");
        }

        await worker.StopAsync(CancellationToken.None); // a real shutdown still ends it
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition not met within {timeout}.");
    }

    // a broker that accepted the connection but never confirms: waits until cancelled.
    private sealed class HungPublisher(Action hit) : IEventPublisher
    {
        public async Task PublishAsync(string messageId, string type, string payload, CancellationToken ct)
        {
            hit();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}
