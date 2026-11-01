using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Features.Ingestion;
using Microsoft.AspNetCore.Mvc;

namespace ICMarkets.FunctionalTests;

public sealed class IngestionAndHistoryTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public IngestionAndHistoryTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Ingest_ThenHistory_ReturnsTheIngestedSnapshot()
    {
        var ingestResponse = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);
        ingestResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var ingestResult = await ingestResponse.Content.ReadFromJsonAsync<IngestAllChainsResult>();
        ingestResult!.Succeeded.Should().Contain(s => s.Network == "btc-main" && s.Height == 973576);
        ingestResult.Failed.Should().BeEmpty();

        var historyResponse = await _client.GetAsync("/api/v1/blockchains/btc-main/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await historyResponse.Content.ReadFromJsonAsync<HistoryPage>();
        history!.Items.Should().Contain(s => s.Height == 973576 && s.Hash.StartsWith("0000000000000000"));
    }

    [Fact]
    public async Task History_NewestFirst_AndPaginatesWithACursor()
    {
        // two ingests of the same (static) stub body still produce two rows
        // with distinct, increasing CreatedAt - enough to assert ordering
        // without needing a scripted WireMock scenario.
        (await _client.PostAsync("/api/v1/blockchains/ingest", content: null)).EnsureSuccessStatusCode();
        (await _client.PostAsync("/api/v1/blockchains/ingest", content: null)).EnsureSuccessStatusCode();

        var full = await (await _client.GetAsync("/api/v1/blockchains/eth-main/history?take=200"))
            .Content.ReadFromJsonAsync<HistoryPage>();
        full!.Items.Count.Should().BeGreaterThanOrEqualTo(2);
        full.Items.Should().BeInDescendingOrder(s => s.CreatedAt);

        // first page of one: newest row, plus a cursor to continue
        var page1 = await (await _client.GetAsync("/api/v1/blockchains/eth-main/history?take=1"))
            .Content.ReadFromJsonAsync<HistoryPage>();
        page1!.Items.Should().ContainSingle();
        page1.Items[0].CreatedAt.Should().Be(full.Items[0].CreatedAt);
        page1.NextCursor.Should().NotBeNull();

        // second page via the cursor: the next row, with no overlap
        var page2 = await (await _client.GetAsync($"/api/v1/blockchains/eth-main/history?take=1&cursor={page1.NextCursor}"))
            .Content.ReadFromJsonAsync<HistoryPage>();
        page2!.Items.Should().ContainSingle();
        page2.Items[0].CreatedAt.Should().Be(full.Items[1].CreatedAt);
        page2.Items[0].Id.Should().NotBe(page1.Items[0].Id);
    }

    [Fact]
    public async Task History_MalformedCursor_Returns400ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/blockchains/btc-main/history?cursor=not-a-valid-cursor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("Cursor");
    }

    [Fact]
    public async Task Latest_ReturnsTheMostRecentlyIngestedSnapshot_ForThatNetwork()
    {
        (await _client.PostAsync("/api/v1/blockchains/ingest", content: null)).EnsureSuccessStatusCode();

        var latestResponse = await _client.GetAsync("/api/v1/blockchains/ltc-main/latest");
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var latest = await latestResponse.Content.ReadFromJsonAsync<BlockchainSnapshotDto>();
        latest!.Network.Should().Be("ltc-main");
        latest.Height.Should().Be(3203281);
    }

    [Fact]
    public async Task History_UnknownNetwork_Returns400ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/blockchains/doge-main/history");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("Network");
    }
}
