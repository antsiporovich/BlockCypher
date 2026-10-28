using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestChainCommand(Network Network) : IRequest<BlockchainSnapshotDto>;

// Thin: fetch, then delegate persistence to PersistSnapshotCommand so the
// entity-construction/mapping logic lives in exactly one place, shared with
// the batch pipeline in IngestAllChainsCommandHandler.
public sealed class IngestChainCommandHandler(
    IBlockCypherClient client,
    ISender sender) : IRequestHandler<IngestChainCommand, BlockchainSnapshotDto>
{
    public async Task<BlockchainSnapshotDto> Handle(IngestChainCommand request, CancellationToken ct)
    {
        var result = await client.GetChainInfoAsync(request.Network, ct);
        return await sender.Send(new PersistSnapshotCommand(request.Network, result), ct);
    }
}
