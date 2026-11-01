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

    // /history is keyset-paginated: the published schema must describe the { items, nextCursor }
    // envelope and expose a `cursor` query parameter (and no longer a `skip`), so a generated client
    // paginates with the opaque cursor rather than an offset.
    [Fact]
    public async Task HistoryEndpoint_DocumentsTheKeysetEnvelope_AndACursorParameter()
    {
        var schema = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var historyPage = schema.GetProperty("components").GetProperty("schemas").GetProperty("HistoryPage");
        var properties = historyPage.GetProperty("properties");
        properties.TryGetProperty("items", out _).Should().BeTrue("the page exposes its rows under 'items'");
        properties.TryGetProperty("nextCursor", out _).Should().BeTrue("the page exposes the continuation cursor");

        var getHistory = schema.GetProperty("paths")
            .GetProperty("/api/v1/blockchains/{network}/history")
            .GetProperty("get");
        var parameterNames = getHistory.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToList();
        parameterNames.Should().Contain("cursor").And.NotContain("skip");
    }

    // Nullable is not optional: native OpenAPI lists every positional-record property under
    // `required`, so the real JSON must carry each one (as null when it has no value). The
    // serializer used to omit nulls (WhenWritingNull) and so violated its own published schema.
    [Fact]
    public async Task EmptyPage_SatisfiesTheRequiredListOfTheSchema()
    {
        using var fresh = new ApiFactory();
        using var client = fresh.CreateClient();

        var json = await GetJsonAsync(client, "/api/v1/blockchains/btc-main/history");

        json.GetProperty("items").GetArrayLength().Should().Be(0);
        json.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        await AssertConformsAsync(client, "HistoryPage", json);
    }

    [Fact]
    public async Task LastPage_BtcSnapshot_AndEthSnapshot_SatisfyTheRequiredListOfTheSchema()
    {
        // own factory: the class fixture is shared and another test leaves eth-main stubbed as failing
        using var fresh = new ApiFactory();
        using var client = fresh.CreateClient();

        (await client.PostAsync("/api/v1/blockchains/ingest", content: null)).EnsureSuccessStatusCode();

        // last page: nextCursor is null but present
        var btc = await GetJsonAsync(client, "/api/v1/blockchains/btc-main/history?take=200");
        btc.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        var btcItem = btc.GetProperty("items")[0];
        btcItem.GetProperty("gasPrices").ValueKind.Should().Be(JsonValueKind.Null, "BTC has no gas family");
        await AssertConformsAsync(client, "HistoryPage", btc);

        var eth = await GetJsonAsync(client, "/api/v1/blockchains/eth-main/history?take=200");
        var ethItem = eth.GetProperty("items")[0];
        ethItem.GetProperty("feeRates").ValueKind.Should().Be(JsonValueKind.Null, "ETH has no fee family");
        await AssertConformsAsync(client, "HistoryPage", eth);

        await AssertConformsAsync(client, "BlockchainSnapshotDto",
            await GetJsonAsync(client, "/api/v1/blockchains/btc-main/latest"));
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task AssertConformsAsync(HttpClient client, string schemaName, JsonElement value)
    {
        var doc = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schemas = doc.GetProperty("components").GetProperty("schemas");
        var missing = new List<string>();
        CollectMissingRequired(schemas, schemas.GetProperty(schemaName), value, schemaName, missing);
        missing.Should().BeEmpty("every property the schema marks required must be present in the real response");
    }

    // Walks the value against the schema: object -> required + each described property;
    // array -> items; $ref -> the referenced component; oneOf/anyOf (the nullable wrapper) -> the
    // first branch that is not the null type. A null value ends the walk.
    private static void CollectMissingRequired(
        JsonElement schemas, JsonElement schema, JsonElement value, string path, List<string> missing)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            CollectMissingRequired(schemas, schemas.GetProperty(name), value, path, missing);
            return;
        }

        foreach (var key in new[] { "oneOf", "anyOf" })
        {
            if (!schema.TryGetProperty(key, out var branches)) continue;
            foreach (var branch in branches.EnumerateArray())
            {
                if (branch.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "null")
                    continue;
                CollectMissingRequired(schemas, branch, value, path, missing);
                return;
            }
            return;
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var element in value.EnumerateArray())
                CollectMissingRequired(schemas, items, element, $"{path}[{i++}]", missing);
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("required", out var required))
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                if (!value.TryGetProperty(name, out _))
                    missing.Add($"{path}.{name}");

        if (schema.TryGetProperty("properties", out var properties))
            foreach (var property in properties.EnumerateObject())
                if (value.TryGetProperty(property.Name, out var child))
                    CollectMissingRequired(schemas, property.Value, child, $"{path}.{property.Name}", missing);
    }
}
