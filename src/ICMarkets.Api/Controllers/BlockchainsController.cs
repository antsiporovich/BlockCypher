using ICMarkets.Application.Features.History;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Application.Features.Networks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ICMarkets.Api.Controllers;

[ApiController]
[Route("api/v1/blockchains")]
public class BlockchainsController(ISender sender) : ControllerBase
{
    [HttpPost("ingest")]
    public async Task<IActionResult> IngestAll(CancellationToken ct)
    {
        var result = await sender.Send(new IngestAllChainsCommand(), ct);
        return Ok(result);
    }

    [HttpGet("{network}/history")]
    public async Task<IActionResult> History(string network, [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken ct = default)
    {
        var history = await sender.Send(new GetChainHistoryQuery(network, take, skip), ct);
        return Ok(history);
    }

    [HttpGet("{network}/latest")]
    public async Task<IActionResult> Latest(string network, CancellationToken ct)
    {
        var snapshot = await sender.Send(new GetLatestSnapshotQuery(network), ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    [HttpGet("networks")]
    public async Task<IActionResult> Networks(CancellationToken ct)
    {
        var networks = await sender.Send(new GetNetworksQuery(), ct);
        return Ok(networks);
    }
}
