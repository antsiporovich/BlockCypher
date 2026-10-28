using System.Text.Json;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.BlockCypher;

public class BlockCypherClient(HttpClient http, IOptions<BlockCypherOptions> options) : IBlockCypherClient
{
    public async Task<ChainInfoResult> GetChainInfoAsync(Network network, CancellationToken ct)
    {
        if (!options.Value.Networks.TryGetValue(network, out var path))
            throw new InvalidOperationException($"No BlockCypher path configured for network '{network}'.");

        var raw = await http.GetStringAsync(path, ct);
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(raw)
            ?? throw new InvalidOperationException($"BlockCypher returned no data for '{path}'.");
        return new ChainInfoResult(dto, raw);
    }
}
