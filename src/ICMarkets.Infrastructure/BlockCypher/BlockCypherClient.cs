using System.Text.Json;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.BlockCypher;

public class BlockCypherClient(HttpClient http, IOptions<BlockCypherOptions> options, TimeProvider clock) : IChainInfoSource
{
    public async Task<ChainInfoResult> GetChainInfoAsync(Network network, CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.Networks.TryGetValue(network, out var path))
            throw new InvalidOperationException($"No BlockCypher path configured for network '{network}'.");

        var requestUri = string.IsNullOrWhiteSpace(opts.Token) ? path : $"{path}?token={opts.Token}";

        // One explicit budget for the whole fetch, passed into the pipeline. The body is
        // buffered inside the resilience retry (ResponseBufferingHandler), so a transport
        // drop mid-body is retried by that same policy. This token still bounds a body
        // that stalls - LoadIntoBufferAsync honours it - and it also bounds headers and retries.
        var budget = TimeSpan.FromSeconds(Math.Max(opts.FetchTimeoutSeconds, 1));
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(budget);

        string raw;
        try
        {
            raw = await http.GetStringAsync(requestUri, budgetCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // OUR budget fired (the caller's token is not cancelled): this is a
            // per-network failure, not a shutdown. Say so with a non-cancellation type
            // so no "is not OperationCanceledException" filter can mistake it.
            throw new TimeoutException(
                $"BlockCypher did not complete the request for '{path}' within {budget.TotalSeconds:0}s.");
        }
        var dto = JsonSerializer.Deserialize<BlockCypherChainInfoDto>(raw)
            ?? throw new InvalidOperationException($"BlockCypher returned no data for '{path}'.");

        // a 200 with a junk/empty/foreign body is a fetch failure, not a snapshot.
        ChainInfoValidation.EnsureValid(network, dto, clock.GetUtcNow());

        // adapter output: the neutral summary (Application never sees the wire DTO) + the raw payload.
        return new ChainInfoResult(BlockCypherChainInfoMapper.ToChainInfo(dto), raw);
    }
}
