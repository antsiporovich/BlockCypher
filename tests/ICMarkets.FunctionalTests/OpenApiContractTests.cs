using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace ICMarkets.FunctionalTests;

// The wire contract and the published schema must say the same thing. The failure entries'
// network used to be an enum: the MVC serializer wrote "EthMain" while native OpenAPI
// documented an integer, so a client generated from /openapi/v1.json could not read the
// normal "one network failed" response - and success used "eth-main" for the same network.
public sealed class OpenApiContractTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public OpenApiContractTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FailedNetwork_IsAStringKey_InTheResponse_AndInTheOpenApiSchema()
    {
        _factory.StubNetwork("/eth/main", jsonBody: "{\"error\":\"boom\"}", statusCode: 500);

        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var failedNetwork = body.RootElement.GetProperty("failed")[0].GetProperty("network");
        failedNetwork.ValueKind.Should().Be(JsonValueKind.String);
        failedNetwork.GetString().Should().Be("eth-main", "failures use the same key as the successful snapshots");

        var succeededNetworks = body.RootElement.GetProperty("succeeded").EnumerateArray()
            .Select(s => s.GetProperty("network").GetString()).ToList();
        succeededNetworks.Should().NotContain("eth-main").And.OnlyContain(n => n!.Contains('-'));

        var schema = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var networkSchema = schema.GetProperty("components").GetProperty("schemas")
            .GetProperty("IngestFailure").GetProperty("properties").GetProperty("network");
        networkSchema.GetProperty("type").GetString().Should().Be("string",
            "the schema must not describe the failed network as an integer");
    }
}
