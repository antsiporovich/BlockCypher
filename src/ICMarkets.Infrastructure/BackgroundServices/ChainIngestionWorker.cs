using ICMarkets.Application.Features.Ingestion;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.BackgroundServices;

// Periodically re-runs IngestAllChainsCommand on a configurable interval.
// BackgroundService itself is a singleton, but MediatR/AppDbContext are
// scoped, so each tick gets its own DI scope - same rule as the per-network
// scoping in IngestAllChainsCommandHandler.
public sealed class ChainIngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<ChainIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("ChainIngestionWorker is disabled (Ingestion:Enabled = false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(opts.IntervalSeconds, 30));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var result = await sender.Send(new IngestAllChainsCommand(), stoppingToken);
                logger.LogInformation(
                    "Scheduled ingestion run: {Succeeded} succeeded, {Failed} failed.",
                    result.Succeeded.Count, result.Failed.Count);
            }
            // Only a cancelled stoppingToken means "shut down". Any other exception -
            // including a stray timeout, which is an OperationCanceledException too -
            // must NOT escape ExecuteAsync: the Generic Host would treat that as a
            // fatal BackgroundService failure and stop the whole application.
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Scheduled ingestion run failed unexpectedly.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
