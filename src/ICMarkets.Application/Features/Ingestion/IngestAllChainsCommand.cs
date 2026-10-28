using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestAllChainsCommand : IRequest<IngestAllChainsResult>;

public sealed record IngestFailure(Network Network, string Error);

public sealed record IngestAllChainsResult(
    IReadOnlyList<BlockchainSnapshotDto> Succeeded,
    IReadOnlyList<IngestFailure> Failed);

public sealed class IngestAllChainsCommandHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestAllChainsCommandHandler> logger)
    : IRequestHandler<IngestAllChainsCommand, IngestAllChainsResult>
{
    // Task.WhenAll across all 5 networks. Each branch gets its own DI scope
    // (and so its own AppDbContext/ISender) rather than sharing the ambient
    // scoped ISender - DbContext isn't thread-safe, so concurrent SaveChanges
    // calls on one shared instance would throw. Per-network failures are
    // caught locally and never abort the batch.
    // shortcut: concurrency here is unbounded (only 5 networks, so 5
    // concurrent HTTP calls is harmless). Phase 5b replaces this with a
    // bounded Channels + Parallel.ForEachAsync pipeline for the general case.
    public async Task<IngestAllChainsResult> Handle(IngestAllChainsCommand request, CancellationToken ct)
    {
        var tasks = Enum.GetValues<Network>().Select(network => IngestOneAsync(network, ct));
        var results = await Task.WhenAll(tasks);

        var succeeded = results.Where(r => r.Snapshot is not null).Select(r => r.Snapshot!).ToList();
        var failed = results.Where(r => r.Failure is not null).Select(r => r.Failure!).ToList();

        return new IngestAllChainsResult(succeeded, failed);
    }

    private async Task<(BlockchainSnapshotDto? Snapshot, IngestFailure? Failure)> IngestOneAsync(Network network, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        try
        {
            var dto = await sender.Send(new IngestChainCommand(network), ct);
            return (dto, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ingestion failed for {Network}", network);
            return (null, new IngestFailure(network, ex.Message));
        }
    }
}
