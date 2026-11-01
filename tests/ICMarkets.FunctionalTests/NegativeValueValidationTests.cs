using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Features.Ingestion;

namespace ICMarkets.FunctionalTests;

// Its own factory (so its own WireMock stubs and in-memory DB) - stubbing btc/main to return
// negative fees must not leak into the other classes' happy-path assertions.
public sealed class NegativeValueValidationTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public NegativeValueValidationTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_UpstreamWithNegativeFees_IsRejected_NotPersistedAsASnapshot()
    {
        // a well-formed 200 that nonetheless carries physically-impossible negative fees must be
        // treated as a bad body (failure isolation), never stored as a snapshot.
        _factory.StubNetwork("/btc/main", jsonBody: """
            {
              "name": "BTC.main",
              "height": 973576,
              "hash": "00000000000000000001dacaff55dc24e332657749d5983fc64a46cd79e6d333",
              "time": "2026-01-01T00:00:00Z",
              "peer_count": 1,
              "unconfirmed_count": 1,
              "high_fee_per_kb": -300,
              "medium_fee_per_kb": -200,
              "low_fee_per_kb": -100,
              "last_fork_height": -1
            }
            """);

        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Failed.Should().ContainSingle(f => f.Network == "btc-main");
        result.Succeeded.Should().NotContain(s => s.Network == "btc-main");

        // and nothing was persisted for btc-main
        var history = await (await _client.GetAsync("/api/v1/blockchains/btc-main/history"))
            .Content.ReadFromJsonAsync<HistoryPage>();
        history!.Items.Should().BeEmpty();
    }
}
