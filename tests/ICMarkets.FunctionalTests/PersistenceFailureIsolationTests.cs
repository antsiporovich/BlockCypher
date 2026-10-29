using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// Unlike the NSubstitute-based unit tests (which replace ISender and so never
// touch EF), this runs the real pipeline against a real AppDbContext on SQLite:
// one network returns a body that violates a NOT NULL column ("name": null),
// so SaveChanges genuinely throws. A separate ApiFactory per class keeps the
// in-memory database free of rows written by other test classes.
public sealed class PersistenceFailureIsolationTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public PersistenceFailureIsolationTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_OneResponseViolatesNotNull_OtherFourStillPersist()
    {
        // valid JSON, HTTP 200, but "name": null -> NOT NULL violation on insert.
        _factory.StubNetwork("/ltc/main", """
            { "name": null, "height": 1, "hash": "abc", "peer_count": 1, "unconfirmed_count": 1 }
            """);

        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == ICMarkets.Domain.Network.LtcMain);

        // the other four rows really are in the database - not just reported as succeeded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Snapshots.CountAsync()).Should().Be(4);
    }
}
