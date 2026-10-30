using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Domain;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// The upstream answers one network with "200 OK" and then never sends the body.
// GetStringAsync reads the body AFTER the resilience pipeline has returned, so only
// the client's own fetch budget can end that wait - and when it does, the failure is
// an OperationCanceledException while the caller's token is NOT cancelled. Before the
// fix that was treated as caller cancellation: it propagated and took the whole batch
// down (0 of 5 instead of 4 of 5). This runs the REAL client registration (resilience
// handler included); only the primary handler is swapped to produce the stalled body.
public sealed class SlowBodyIsolationTests : IClassFixture<SlowBodyApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    public SlowBodyIsolationTests(SlowBodyApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Ingest_OneNetworkStallsItsBody_ThatNetworkFails_OtherFourSucceed()
    {
        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Succeeded.Should().HaveCount(4, "failures were: {0}", string.Join(" | ", result.Failed.Select(f => $"{f.Network}: {f.Error}")));
        result.Failed.Should().ContainSingle(f => f.Network == "ltc-main");
    }
}

public sealed class SlowBodyApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // short budget so the test takes seconds, not the production 45s. Safe at 2s:
            // healthy responses are in-process and instant, only the stalled body waits it out.
            ["BlockCypher:FetchTimeoutSeconds"] = "2",
        }));

        builder.ConfigureServices(services =>
            services.AddHttpClient<IChainInfoSource, BlockCypherClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StalledLtcBodyHandler()));
    }

    // The four healthy networks are answered IN-PROCESS with their fixture, instantly - no
    // socket, no WireMock, nothing whose latency can vary under a parallel test run, so the
    // fetch budget can never be missed by a healthy request. Only /ltc/main gets 200 + headers
    // immediately and a body stream that never produces a byte. The real client, resilience
    // pipeline, rate gate and persistence still run; real HTTP integration is covered by the
    // WireMock-based tests.
    private sealed class StalledLtcBodyHandler : HttpMessageHandler
    {
        private static readonly Dictionary<string, string> Healthy = new()
        {
            ["/btc/main"] = Fixtures.BtcMain,
            ["/eth/main"] = Fixtures.EthMain,
            ["/dash/main"] = Fixtures.DashMain,
            ["/btc/test3"] = Fixtures.BtcTest3,
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/ltc/main", StringComparison.Ordinal))
            {
                var stalled = new StreamContent(new StalledStream());
                stalled.Headers.ContentType = new("application/json");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = stalled });
            }

            var fixture = Healthy.First(kv => path.EndsWith(kv.Key, StringComparison.Ordinal)).Value;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fixture, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
