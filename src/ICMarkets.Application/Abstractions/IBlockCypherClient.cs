using ICMarkets.Application.Dtos;
using ICMarkets.Domain;

namespace ICMarkets.Application.Abstractions;

public interface IBlockCypherClient
{
    Task<ChainInfoResult> GetChainInfoAsync(Network network, CancellationToken ct);
}
