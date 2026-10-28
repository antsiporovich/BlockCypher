using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.History;

public sealed record GetLatestSnapshotQuery(string Network) : IRequest<BlockchainSnapshotDto?>;

public sealed class GetLatestSnapshotQueryHandler(IBlockchainRepository repo, SnapshotMapper mapper)
    : IRequestHandler<GetLatestSnapshotQuery, BlockchainSnapshotDto?>
{
    public async Task<BlockchainSnapshotDto?> Handle(GetLatestSnapshotQuery request, CancellationToken ct)
    {
        var network = NetworkKey.Parse(request.Network);
        var snapshot = await repo.GetLatestAsync(network, ct);
        return snapshot is null ? null : mapper.ToDto(snapshot);
    }
}
