using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Features.Ingestion;

namespace ICMarkets.FunctionalTests;

// HTTP 200 with a body that is not a usable chain summary must surface as a
// per-network failure, never as a stored "success" full of defaults. Each test
// restores the stub it overrides so the tests in this class stay independent.
public sealed class ResponseValidationTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public ResponseValidationTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_Upstream200WithEmptyObject_IsReportedAsFailed_NotSucceeded()
    {
        _factory.StubNetwork("/eth/main", jsonBody: "{}");
        try
        {
            var result = await IngestAsync();

            result.Failed.Should().ContainSingle(f => f.Network == ICMarkets.Domain.Network.EthMain)
                .Which.Error.Should().Contain("Invalid BlockCypher response");
            result.Succeeded.Should().HaveCount(4);
            result.Succeeded.Should().NotContain(s => s.Network == "eth-main");
        }
        finally
        {
            _factory.StubNetwork("/eth/main", Fixtures.EthMain);
        }
    }

    [Fact]
    public async Task Ingest_Upstream200WithAnotherNetworksBody_IsReportedAsFailed()
    {
        // the ETH body (name ETH.main, gas-price family) served for the LTC path.
        _factory.StubNetwork("/ltc/main", Fixtures.EthMain);
        try
        {
            var result = await IngestAsync();

            result.Failed.Should().ContainSingle(f => f.Network == ICMarkets.Domain.Network.LtcMain)
                .Which.Error.Should().Contain("LTC.main");
            result.Succeeded.Should().NotContain(s => s.Network == "ltc-main");
        }
        finally
        {
            _factory.StubNetwork("/ltc/main", Fixtures.LtcMain);
        }
    }

    [Fact]
    public async Task Ingest_Upstream200WithErrorObject_IsReportedAsFailed()
    {
        _factory.StubNetwork("/dash/main", jsonBody: "{\"error\":\"Limits reached.\"}");
        try
        {
            var result = await IngestAsync();

            result.Failed.Should().ContainSingle(f => f.Network == ICMarkets.Domain.Network.DashMain);
            result.Succeeded.Should().NotContain(s => s.Network == "dash-main");
        }
        finally
        {
            _factory.StubNetwork("/dash/main", Fixtures.DashMain);
        }
    }

    [Fact]
    public async Task Ingest_ValidFixtures_StoreTheRealUpstreamTime_NotTheDefault()
    {
        var result = await IngestAsync();

        result.Failed.Should().BeEmpty();
        result.Succeeded.Should().OnlyContain(s => s.Time.Year == 2026);
    }

    private async Task<IngestAllChainsResult> IngestAsync()
    {
        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions))!;
    }
}
