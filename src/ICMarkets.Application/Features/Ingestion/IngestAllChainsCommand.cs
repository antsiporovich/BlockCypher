using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Telemetry;
using ICMarkets.Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestAllChainsCommand : IRequest<IngestAllChainsResult>;

/// <summary>One network's ingestion failure, with the exception message.</summary>
/// <param name="Network">The network key that failed.</param>
/// <param name="Error">The exception message from the failed fetch or persist.</param>
public sealed record IngestFailure(Network Network, string Error);

/// <summary>Result of ingesting all configured networks in one batch.</summary>
/// <param name="Succeeded">Snapshots that were fetched and persisted successfully.</param>
/// <param name="Failed">Networks that failed, with the reason - never aborts the other networks.</param>
public sealed record IngestAllChainsResult(
    IReadOnlyList<BlockchainSnapshotDto> Succeeded,
    IReadOnlyList<IngestFailure> Failed);

// Producer/consumer pipeline over a bounded Channel<T>: a bounded-concurrency
// producer fetches from BlockCypher for all 5 networks, a single consumer
// persists them one at a time. See README.md "Concurrency" for why this is
// Task-based (Channels + Parallel.ForEachAsync) and not PLINQ.
public sealed class IngestAllChainsCommandHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestAllChainsCommandHandler> logger)
    : IRequestHandler<IngestAllChainsCommand, IngestAllChainsResult>
{
    // Bounded, not unbounded fan-out: BlockCypher rate-limits unauthenticated
    // callers, so this caps how many fetches run at once.
    private const int MaxFetchConcurrency = 3;

    // Backpressure valve: once this many fetched-but-not-yet-persisted items
    // are queued, a producer branch blocks on WriteAsync until the single
    // consumer catches up. Not a throughput-tuned queue depth - just enough
    // slack that a fast fetch doesn't idle while the previous item persists.
    private const int ChannelCapacity = 4;

    public async Task<IngestAllChainsResult> Handle(IngestAllChainsCommand request, CancellationToken ct)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ingest-all-chains");
        activity?.SetTag("icmarkets.network_count", Enum.GetValues<Network>().Length);

        var channel = Channel.CreateBounded<FetchedChain>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var failures = new ConcurrentBag<IngestFailure>();

        var producing = ProduceAsync(channel.Writer, failures, ct);
        var succeeded = await ConsumeAsync(channel.Reader, failures, ct);
        await producing; // surfaces a producer-level exception (e.g. cancellation) to the caller

        activity?.SetTag("icmarkets.succeeded_count", succeeded.Count);
        activity?.SetTag("icmarkets.failed_count", failures.Count);

        return new IngestAllChainsResult(succeeded, failures.ToList());
    }

    // Parallel.ForEachAsync, not PLINQ: this work is I/O-bound (waiting on
    // HTTP), not CPU-bound, so there's nothing for a CPU-partitioning LINQ
    // operator to parallelize - it would just block thread-pool threads on
    // I/O. Parallel.ForEachAsync with an async delegate and an explicit
    // MaxDegreeOfParallelism is the correct, bounded tool for this.
    private async Task ProduceAsync(ChannelWriter<FetchedChain> writer, ConcurrentBag<IngestFailure> failures, CancellationToken ct)
    {
        try
        {
            await Parallel.ForEachAsync(
                Enum.GetValues<Network>(),
                new ParallelOptions { MaxDegreeOfParallelism = MaxFetchConcurrency, CancellationToken = ct },
                async (network, token) =>
                {
                    // fresh DI scope per branch: IBlockCypherClient itself is
                    // safe to share, but this keeps the same scoping discipline
                    // as the consumer side and avoids any future dependency on
                    // this client becoming scoped/stateful surprising someone.
                    using var scope = scopeFactory.CreateScope();
                    var client = scope.ServiceProvider.GetRequiredService<IBlockCypherClient>();
                    var networkKey = NetworkKey.ToKey(network);

                    using var fetchActivity = IngestionTelemetry.ActivitySource.StartActivity("fetch-chain-info");
                    fetchActivity?.SetTag("icmarkets.network", networkKey);

                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        var result = await client.GetChainInfoAsync(network, token);
                        await writer.WriteAsync(new FetchedChain(network, result), token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Fetch failed for {Network}", network);
                        failures.Add(new IngestFailure(network, ex.Message));
                        IngestionTelemetry.IngestionFailures.Add(1, IngestionTelemetry.NetworkTag(networkKey));
                    }
                    finally
                    {
                        IngestionTelemetry.FetchDuration.Record(stopwatch.Elapsed.TotalMilliseconds, IngestionTelemetry.NetworkTag(networkKey));
                    }
                });
        }
        finally
        {
            // always complete the writer, even on cancellation or an
            // unexpected exception, so the consumer's ReadAllAsync loop
            // terminates instead of hanging forever.
            writer.Complete();
        }
    }

    private async Task<List<BlockchainSnapshotDto>> ConsumeAsync(
        ChannelReader<FetchedChain> reader, ConcurrentBag<IngestFailure> failures, CancellationToken ct)
    {
        // Single consumer: every persist goes through one DI scope (one
        // AppDbContext) sequentially, so there's no concurrent SaveChanges
        // and no extra locking needed - DbContext just never sees two
        // operations in flight at once.
        var succeeded = new List<BlockchainSnapshotDto>();
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await foreach (var item in reader.ReadAllAsync(ct))
        {
            try
            {
                var dto = await sender.Send(new PersistSnapshotCommand(item.Network, item.Result), ct);
                succeeded.Add(dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Persist failed for {Network}", item.Network);
                failures.Add(new IngestFailure(item.Network, ex.Message));
                IngestionTelemetry.IngestionFailures.Add(1, IngestionTelemetry.NetworkTag(NetworkKey.ToKey(item.Network)));
            }
        }

        return succeeded;
    }

    private sealed record FetchedChain(Network Network, ChainInfoResult Result);
}
