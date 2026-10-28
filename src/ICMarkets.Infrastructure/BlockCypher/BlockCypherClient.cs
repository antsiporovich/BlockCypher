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
        var opts = options.Value;
        if (!opts.Networks.TryGetValue(network, out var path))
            throw new InvalidOperationException($"No BlockCypher path configured for network '{network}'.");

        var requestUri = string.IsNullOrWhiteSpace(opts.Token) ? path : $"{path}?token={opts.Token}";

        var raw = await http.GetStringAsync(requestUri, ct);
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(raw)
            ?? throw new InvalidOperationException($"BlockCypher returned no data for '{path}'.");
        return new ChainInfoResult(dto, raw);
    }
}
