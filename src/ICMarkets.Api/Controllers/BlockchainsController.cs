using ICMarkets.Application.Abstractions;
using ICMarkets.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ICMarkets.Api.Controllers;

[ApiController]
[Route("api/v1/blockchains")]
public class BlockchainsController(IBlockCypherClient client, IBlockchainRepository repo, TimeProvider clock) : ControllerBase
{
    // ponytail: single network wired here on purpose - Phase 5 loops all 5 networks
    // in parallel (Channels + Parallel.ForEachAsync). Route shape stays stable across phases.
    private const string Network = "btc-main";

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest(CancellationToken ct)
    {
        var result = await client.GetChainInfoAsync("btc/main", ct);
        var info = result.Dto;

        var snapshot = new BlockchainSnapshot
        {
            Network = Network,
            Name = info.Name,
            Height = info.Height,
            Hash = info.Hash,
            Time = info.Time,
            LatestUrl = info.LatestUrl,
            PreviousHash = info.PreviousHash,
            PreviousUrl = info.PreviousUrl,
            PeerCount = info.PeerCount,
            UnconfirmedCount = info.UnconfirmedCount,
            HighFeePerKb = info.HighFeePerKb,
            MediumFeePerKb = info.MediumFeePerKb,
            LowFeePerKb = info.LowFeePerKb,
            LastForkHeight = info.LastForkHeight,
            LastForkHash = info.LastForkHash,
            RawJson = result.RawJson,
            CreatedAt = clock.GetUtcNow(),
        };

        await repo.AddAsync(snapshot, ct);
        return Ok(snapshot);
    }

    [HttpGet("{network}/history")]
    public async Task<IActionResult> History(string network, [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken ct = default)
    {
        var history = await repo.GetHistoryAsync(network, take, skip, ct);
        return Ok(history);
    }
}
