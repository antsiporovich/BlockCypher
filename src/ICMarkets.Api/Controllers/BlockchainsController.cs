using ICMarkets.Application.Dtos;
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
    /// <summary>
    /// Ingests a fresh network summary for all configured chains (btc/main, eth/main,
    /// dash/main, btc/test3, ltc/main) and stores each one with a <c>CreatedAt</c> timestamp.
    /// </summary>
    /// <remarks>
    /// Fetches are bounded-concurrency (not one request per network at once) and each
    /// network is isolated - one chain failing (BlockCypher error, rate limit, timeout)
    /// never aborts the others. Check <c>failed</c> in the response for any that didn't make it.
    /// </remarks>
    /// <response code="200">Always 200 if the request itself is valid - check the response body for per-network results.</response>
    [HttpPost("ingest")]
    [ProducesResponseType<IngestAllChainsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IngestAll(CancellationToken ct)
    {
        var result = await sender.Send(new IngestAllChainsCommand(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns stored snapshots for one network, newest first.
    /// </summary>
    /// <param name="network">Network key, e.g. <c>btc-main</c>, <c>eth-main</c>, <c>dash-main</c>, <c>btc-test3</c>, <c>ltc-main</c>.</param>
    /// <param name="take">Page size (1-200). Defaults to 50.</param>
    /// <param name="skip">Number of rows to skip for paging. Defaults to 0.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The page of stored snapshots, ordered by <c>CreatedAt</c> descending.</response>
    /// <response code="400">The network key is unrecognized, or <c>take</c>/<c>skip</c> is out of range.</response>
    [HttpGet("{network}/history")]
    [ProducesResponseType<List<BlockchainSnapshotDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> History(string network, [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken ct = default)
    {
        var history = await sender.Send(new GetChainHistoryQuery(network, take, skip), ct);
        return Ok(history);
    }

    /// <summary>
    /// Returns the most recently stored snapshot for one network.
    /// </summary>
    /// <param name="network">Network key, e.g. <c>btc-main</c>, <c>eth-main</c>, <c>dash-main</c>, <c>btc-test3</c>, <c>ltc-main</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The latest stored snapshot for the network.</response>
    /// <response code="400">The network key is unrecognized.</response>
    /// <response code="404">The network is valid but nothing has been ingested for it yet.</response>
    [HttpGet("{network}/latest")]
    [ProducesResponseType<BlockchainSnapshotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Latest(string network, CancellationToken ct)
    {
        var snapshot = await sender.Send(new GetLatestSnapshotQuery(network), ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    /// <summary>
    /// Lists the network keys this API accepts (the same keys used in the <c>{network}</c> route
    /// parameter above).
    /// </summary>
    /// <response code="200">The supported network keys.</response>
    [HttpGet("networks")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Networks(CancellationToken ct)
    {
        var networks = await sender.Send(new GetNetworksQuery(), ct);
        return Ok(networks);
    }
}
