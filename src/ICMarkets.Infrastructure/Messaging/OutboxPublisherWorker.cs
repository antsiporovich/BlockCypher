using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.Messaging;

// Polls the outbox table on a timer and publishes each unprocessed row to
// RabbitMQ, marking it processed only after a successful publish - same
// scoped-DbContext-per-tick shape as ChainIngestionWorker. If RabbitMQ is
// down, publishing throws, the row stays unprocessed, and the next tick
// retries it - at-least-once delivery, which is the point of an outbox.
public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("OutboxPublisherWorker is disabled (Outbox:Enabled = false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(opts.PollIntervalSeconds, 1));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

                var pending = await db.Outbox
                    .Where(m => m.ProcessedAt == null)
                    .OrderBy(m => m.Id)
                    .Take(opts.BatchSize)
                    .ToListAsync(stoppingToken);

                foreach (var message in pending)
                {
                    // one failed publish stops this tick - don't mark later
                    // rows processed out of order, don't swallow the error,
                    // just retry the whole batch next tick.
                    await publisher.PublishAsync(message.Type, message.Payload, stoppingToken);
                    message.MarkProcessed(DateTimeOffset.UtcNow);
                }

                if (pending.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Published {Count} outbox message(s).", pending.Count);
                }

                // Retention: purge processed rows past the configured window so
                // the table stays bounded under real traffic instead of growing
                // forever - publishing alone never deletes a row.
                var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Max(opts.RetentionHours, 1));
                var purged = await db.Outbox
                    .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (purged > 0)
                    logger.LogInformation("Purged {Count} processed outbox message(s) older than {RetentionHours}h.", purged, opts.RetentionHours);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // RabbitMQ unreachable, etc. - rows stay unprocessed, retried
                // next tick. Never let this loop die.
                logger.LogError(ex, "Outbox publish run failed unexpectedly.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
