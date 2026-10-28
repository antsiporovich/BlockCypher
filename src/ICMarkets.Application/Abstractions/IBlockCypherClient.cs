using ICMarkets.Application.Dtos;

namespace ICMarkets.Application.Abstractions;

public interface IBlockCypherClient
{
    Task<ChainInfoResult> GetChainInfoAsync(string coinChainPath, CancellationToken ct);
}
