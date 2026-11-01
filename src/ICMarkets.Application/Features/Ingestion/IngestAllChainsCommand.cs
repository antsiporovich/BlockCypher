using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Telemetry;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestAllChainsCommand : IRequest<IngestAllChainsResult>;

/// <summary>One network's ingestion failure, with the exception message.</summary>
/// <param name="Network">The network key that failed (e.g. <c>eth-main</c>) - the same key format as <c>BlockchainSnapshotDto.Network</c>.</param>
/// <param name="Error">The exception message from the failed fetch or persist.</param>
// The key is a string on purpose: an enum here would put an integer-or-string ambiguity into the
// wire contract (native OpenAPI documents enums as integers, the MVC converter writes names) and
// would make success and failure use different spellings of the same network.
public sealed record IngestFailure(string Network, string Error);

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
    IChainInfoSource chainInfoSource,
    IUnitOfWorkFactory unitOfWorkFactory,
    SnapshotMapper mapper,
    TimeProvider clock,
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
                    // the source is injected directly: an HttpClient-based adapter is
                    // thread-safe, so the parallel branches simply share it.
                    var networkKey = NetworkKey.ToKey(network);

                    using var fetchActivity = IngestionTelemetry.ActivitySource.StartActivity("fetch-chain-info");
                    fetchActivity?.SetTag("icmarkets.network", networkKey);

                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        // stamped BEFORE the call: CreatedAt is the moment the API was asked,
                        // not when the answer was received/parsed (which varies with latency,
                        // rate-limit queueing and retries) and certainly not when the row is
                        // persisted (later still if the item waits in the bounded channel).
                        var requestedAt = clock.GetUtcNow();
                        var result = await chainInfoSource.GetChainInfoAsync(network, token);
                        await writer.WriteAsync(new FetchedChain(network, result, requestedAt), token);
                    }
                    // Discriminate by TOKEN, not by exception type: a timeout also surfaces
                    // as an OperationCanceledException while `token` is NOT cancelled, and
                    // that is one network failing - not a shutdown. Only a cancelled token
                    // is allowed to propagate and abort the batch.
                    catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Fetch failed for {Network}", network);
                        failures.Add(new IngestFailure(NetworkKey.ToKey(network), ex.Message));
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
        // Single consumer: persists run one at a time, so there's no
        // concurrent SaveChanges and no extra locking needed. But each item
        // gets its OWN unit of work (and so its own AppDbContext): a failed
        // SaveChanges leaves the rejected entity in the ChangeTracker as
        // Added, and a shared context would retry inserting it together with
        // the next valid entity - one bad response would then fail every
        // later network too. A poisoned unit of work is simply discarded.
        // The factory port hides how it is built (a DI scope in Infrastructure).
        // For 5 networks the per-item cost is negligible.
        var succeeded = new List<BlockchainSnapshotDto>();

        await foreach (var item in reader.ReadAllAsync(ct))
        {
            try
            {
                succeeded.Add(await PersistAsync(item, ct));
            }
            // same rule as the fetch side: only a cancelled token aborts the batch.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Persist failed for {Network}", item.Network);
                failures.Add(new IngestFailure(NetworkKey.ToKey(item.Network), ex.Message));
                IngestionTelemetry.IngestionFailures.Add(1, IngestionTelemetry.NetworkTag(NetworkKey.ToKey(item.Network)));
            }
        }

        return succeeded;
    }

    // CreatedAt is the REQUEST time stamped by the producer just before it asked BlockCypher,
    // deliberately not read from the clock here: under backpressure an item can wait in the
    // channel before it is persisted, and the history must reflect when the data was
    // requested, not when the row was written.
    private async Task<BlockchainSnapshotDto> PersistAsync(FetchedChain item, CancellationToken ct)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("persist-snapshot");
        var networkKey = NetworkKey.ToKey(item.Network);
        activity?.SetTag("icmarkets.network", networkKey);

        var info = item.Result.Info;
        var snapshot = BlockchainSnapshot.Create(
            network: item.Network,
            name: info.Name,
            height: info.Height,
            hash: info.Hash,
            time: info.Time,
            latestUrl: info.LatestUrl,
            previousHash: info.PreviousHash,
            previousUrl: info.PreviousUrl,
            peerCount: info.PeerCount,
            unconfirmedCount: info.UnconfirmedCount,
            feeRates: info.FeeRates,
            gasPrices: info.GasPrices,
            lastForkHeight: info.LastForkHeight,
            lastForkHash: info.LastForkHash,
            rawJson: item.Result.RawJson,
            createdAt: item.RequestedAt);

        await using var unitOfWork = unitOfWorkFactory.Create();
        await unitOfWork.Snapshots.AddAsync(snapshot, ct);
        await unitOfWork.SaveChangesAsync(ct);

        IngestionTelemetry.SnapshotsIngested.Add(1, IngestionTelemetry.NetworkTag(networkKey));

        return mapper.ToDto(snapshot);
    }

    private sealed record FetchedChain(Network Network, ChainInfoResult Result, DateTimeOffset RequestedAt);
}
