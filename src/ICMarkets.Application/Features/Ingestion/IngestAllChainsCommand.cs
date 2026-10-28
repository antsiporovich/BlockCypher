using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestAllChainsCommand : IRequest<IngestAllChainsResult>;

public sealed record IngestFailure(Network Network, string Error);

public sealed record IngestAllChainsResult(
    IReadOnlyList<BlockchainSnapshotDto> Succeeded,
    IReadOnlyList<IngestFailure> Failed);

public sealed class IngestAllChainsCommandHandler(ISender sender, ILogger<IngestAllChainsCommandHandler> logger)
    : IRequestHandler<IngestAllChainsCommand, IngestAllChainsResult>
{
    // ponytail: sequential for now - Phase 5 upgrades this loop to bounded
    // Channels + Parallel.ForEachAsync with a fresh DI scope per branch
    // (DbContext isn't thread-safe). Route shape and per-network isolation
    // (try/catch per network, no partial-failure abort) already match the
    // Phase 5 contract, so only the loop body changes.
    public async Task<IngestAllChainsResult> Handle(IngestAllChainsCommand request, CancellationToken ct)
    {
        var succeeded = new List<BlockchainSnapshotDto>();
        var failed = new List<IngestFailure>();

        foreach (var network in Enum.GetValues<Network>())
        {
            try
            {
                succeeded.Add(await sender.Send(new IngestChainCommand(network), ct));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ingestion failed for {Network}", network);
                failed.Add(new IngestFailure(network, ex.Message));
            }
        }

        return new IngestAllChainsResult(succeeded, failed);
    }
}
