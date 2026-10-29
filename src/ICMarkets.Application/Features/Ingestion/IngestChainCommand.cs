using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.Ingestion;

// shortcut: no API endpoint calls this directly - the assessment only asks
// for the batch POST /ingest (IngestAllChainsCommand), which fans out per
// network itself via the Channel pipeline rather than going through this
// command. Kept for CQRS symmetry with the per-network GetChainHistoryQuery
// on the read side; not wired to its own endpoint or test since the
// assessment doesn't require per-network ingest.
public sealed record IngestChainCommand(Network Network) : IRequest<BlockchainSnapshotDto>;

// Thin: fetch, then delegate persistence to PersistSnapshotCommand so the
// entity-construction/mapping logic lives in exactly one place, shared with
// the batch pipeline in IngestAllChainsCommandHandler.
public sealed class IngestChainCommandHandler(
    IBlockCypherClient client,
    ISender sender,
    TimeProvider clock) : IRequestHandler<IngestChainCommand, BlockchainSnapshotDto>
{
    public async Task<BlockchainSnapshotDto> Handle(IngestChainCommand request, CancellationToken ct)
    {
        var result = await client.GetChainInfoAsync(request.Network, ct);
        return await sender.Send(new PersistSnapshotCommand(request.Network, result, clock.GetUtcNow()), ct);
    }
}
