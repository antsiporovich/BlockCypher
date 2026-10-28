using System.Text.Json;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;

namespace ICMarkets.Infrastructure.BlockCypher;

public class BlockCypherClient(HttpClient http) : IBlockCypherClient
{
    public async Task<ChainInfoResult> GetChainInfoAsync(string coinChainPath, CancellationToken ct)
    {
        var raw = await http.GetStringAsync(coinChainPath, ct);
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(raw)
            ?? throw new InvalidOperationException($"BlockCypher returned no data for '{coinChainPath}'.");
        return new ChainInfoResult(dto, raw);
    }
}
