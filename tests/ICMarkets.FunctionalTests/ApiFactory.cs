using ICMarkets.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ICMarkets.FunctionalTests;

// Boots the real API (Program.cs, via the `public partial class Program { }`
// marker) with two swaps: the BlockCypher HttpClient points at a local
// WireMock.Net server instead of the real BlockCypher API, and AppDbContext
// points at a single open in-memory SQLite connection instead of the
// SQLite-file/SQL-Server config the app would otherwise pick. Everything
// else (controllers, validation, MediatR pipeline, CORS, health checks)
// runs unmodified - this is a real end-to-end test of the composed app.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    public WireMockServer WireMock { get; } = WireMockServer.Start();

    public ApiFactory()
    {
        StubNetwork("/btc/main", Fixtures.BtcMain);
        StubNetwork("/eth/main", Fixtures.EthMain);
        StubNetwork("/dash/main", Fixtures.DashMain);
        StubNetwork("/btc/test3", Fixtures.BtcTest3);
        StubNetwork("/ltc/main", Fixtures.LtcMain);
    }

    /// <summary>Replaces (or adds) the WireMock stub for one BlockCypher network path.</summary>
    public void StubNetwork(string path, string jsonBody, int statusCode = 200) =>
        WireMock
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlockCypher:BaseUrl"] = WireMock.Url + "/",
            // the periodic background worker would otherwise fire an ingest
            // run on startup (see ChainIngestionWorker) and race with the
            // test that's asserting on a specific ingest call.
            ["Ingestion:Enabled"] = "false",
        }));

        builder.ConfigureServices(services =>
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            WireMock.Stop();
            WireMock.Dispose();
            _connection?.Dispose();
        }
    }
}
