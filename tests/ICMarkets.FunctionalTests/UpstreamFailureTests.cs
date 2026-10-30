using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Features.Ingestion;

namespace ICMarkets.FunctionalTests;

// A separate ApiFactory/WireMock instance per class, not shared with
// IngestionAndHistoryTests, so stubbing one network to fail here can't
// affect the happy-path assertions running in the other class.
public sealed class UpstreamFailureTests : IClassFixture<ApiFactory>
{
    // ReadFromJsonAsync<T>() with no options implicitly uses
    // JsonSerializerDefaults.Web (camelCase, case-insensitive property
    // matching) - start from that same baseline, don't just add a bare
    // JsonSerializerOptions(), or "succeeded"/"failed" stop matching
    // Succeeded/Failed. Then layer on JsonStringEnumConverter, which the API
    // adds in Program.cs but ReadFromJsonAsync's defaults don't include.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public UpstreamFailureTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_OneUpstream500_DegradesGracefully_InsteadOfFailingTheWholeRequest()
    {
        _factory.StubNetwork("/eth/main", jsonBody: "{\"error\":\"boom\"}", statusCode: 500);

        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        // the API call itself must not surface as a 500 - per-network
        // failure isolation is the point of the ingestion pipeline.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == "eth-main");
    }
}
