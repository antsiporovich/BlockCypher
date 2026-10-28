using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.Networks;

public sealed record GetNetworksQuery : IRequest<IReadOnlyList<string>>;

public sealed class GetNetworksQueryHandler : IRequestHandler<GetNetworksQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetNetworksQuery request, CancellationToken ct) =>
        Task.FromResult(NetworkKey.AllKeys);
}
