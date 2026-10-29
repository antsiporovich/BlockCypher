using AwesomeAssertions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.BackgroundServices;
using ICMarkets.Infrastructure.Messaging;
using ICMarkets.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ICMarkets.IntegrationTests;

// A timeout inside a worker tick is an OperationCanceledException (HttpClient and
// friends throw TaskCanceledException) while the host's stoppingToken is NOT
// cancelled. A filter of the form "is not OperationCanceledException" lets it escape
// ExecuteAsync, and the Generic Host treats an escaped exception from a
// BackgroundService as fatal and stops the whole application. These tests fail if
// that filter comes back: the worker's ExecuteTask must keep running.
public sealed class WorkerTimeoutResilienceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task IngestionWorker_StrayTimeoutInATick_DoesNotEscape_AndHostKeepsRunning()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<WorkerTimeoutResilienceTests>());
        services.AddSingleton(new Counter(() => Interlocked.Increment(ref calls)));
        await using var provider = services.BuildServiceProvider();

        var worker = new ChainIngestionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IngestionOptions { Enabled = true, IntervalSeconds = 3600 }),
            NullLogger<ChainIngestionWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref calls) >= 1);
        await Task.Delay(300); // give a (wrongly) escaped exception time to fault the task

        worker.ExecuteTask!.IsCompleted.Should().BeFalse(
            "a timeout that is not a shutdown must be logged and survived, not allowed to stop the host");

        await worker.StopAsync(CancellationToken.None); // a real shutdown still ends it cleanly
    }

    [Fact]
    public async Task OutboxWorker_StrayTimeoutFromThePublisher_DoesNotEscape_AndHostKeepsRunning()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var attempts = 0;
        var options = Options.Create(new OutboxOptions { Enabled = true, PollIntervalSeconds = 1, BatchSize = 10 });
        var services = new ServiceCollection();
        services.AddSingleton<IPublisher>(new NoOpPublisher());
        services.AddSingleton(options);
        services.AddSingleton<IEventPublisher>(new TimeoutPublisher(() => Interlocked.Increment(ref attempts)));
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
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2); // survived one timeout and ticked again

        worker.ExecuteTask!.IsCompleted.Should().BeFalse();

        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition not met within {Timeout}.");
    }

    private sealed record Counter(Action Hit);

    // stands in for an ingestion run whose HttpClient timed out.
    private sealed class TimeoutThrowingHandler(Counter counter)
        : IRequestHandler<IngestAllChainsCommand, IngestAllChainsResult>
    {
        public Task<IngestAllChainsResult> Handle(IngestAllChainsCommand request, CancellationToken ct)
        {
            counter.Hit();
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
        }
    }

    private sealed class TimeoutPublisher(Action hit) : IEventPublisher
    {
        public Task PublishAsync(string messageId, string type, string payload, CancellationToken ct)
        {
            hit();
            throw new TaskCanceledException("broker confirm timed out", new TimeoutException());
        }
    }
}
