using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.History;

public sealed record GetChainHistoryQuery(string Network, int Take = 50, int Skip = 0)
    : IRequest<List<BlockchainSnapshotDto>>;

public sealed class GetChainHistoryQueryHandler(IBlockchainRepository repo, SnapshotMapper mapper)
    : IRequestHandler<GetChainHistoryQuery, List<BlockchainSnapshotDto>>
{
    public async Task<List<BlockchainSnapshotDto>> Handle(GetChainHistoryQuery request, CancellationToken ct)
    {
        var network = NetworkKey.Parse(request.Network); // validator already guaranteed this parses
        var snapshots = await repo.GetHistoryAsync(network, request.Take, request.Skip, ct);
        return snapshots.Select(mapper.ToDto).ToList();
    }
}
