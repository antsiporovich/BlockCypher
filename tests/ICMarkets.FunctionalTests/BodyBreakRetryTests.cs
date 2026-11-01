using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// One network's first attempt returns 200 and then the body stream breaks. That read now
// happens inside the resilience retry, so the same policy that retries an HttpRequestException
// fetches the network again and it recovers. A raw IOException would NOT be retried: the
// standard predicate only treats HttpRequestException (and timeouts) as transient. This runs
// the real client registration; only the primary handler is swapped.
public sealed class BodyBreakRetryTests : IClassFixture<BodyBreakApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly BodyBreakApiFactory _factory;
    private readonly HttpClient _client;

    public BodyBreakRetryTests(BodyBreakApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_BodyBreaksOnce_ThatNetworkIsRetriedAndSucceeds()
    {
        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Succeeded.Should().HaveCount(5, "failures were: {0}", string.Join(" | ", result.Failed.Select(f => $"{f.Network}: {f.Error}")));
        result.Failed.Should().BeEmpty();
        _factory.LtcAttempts.Should().BeGreaterThanOrEqualTo(2, "the broken body must have caused a second GET");
    }
}

public sealed class BodyBreakApiFactory : ApiFactory
{
    private readonly BreakingLtcBodyHandler _handler = new();

    public int LtcAttempts => _handler.Attempts;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.AddHttpClient<IChainInfoSource, BlockCypherClient>()
                .ConfigurePrimaryHttpMessageHandler(() => _handler));
    }

    // Four networks are answered in-process, instantly. /ltc/main breaks its body on the
    // first send and returns the fixture on the next. The counter is the proof a second
    // GET happened, not just that the batch happened to succeed.
    private sealed class BreakingLtcBodyHandler : HttpMessageHandler
    {
        private static readonly Dictionary<string, string> Healthy = new()
        {
            ["/btc/main"] = Fixtures.BtcMain,
            ["/eth/main"] = Fixtures.EthMain,
            ["/dash/main"] = Fixtures.DashMain,
            ["/btc/test3"] = Fixtures.BtcTest3,
        };

        private int _ltcAttempts;

        public int Attempts => Volatile.Read(ref _ltcAttempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/ltc/main", StringComparison.Ordinal))
            {
                var attempt = Interlocked.Increment(ref _ltcAttempts);
                if (attempt == 1)
                {
                    var broken = new StreamContent(new BrokenBodyStream());
                    broken.Headers.ContentType = new("application/json");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = broken });
                }

                return Task.FromResult(Json(Fixtures.LtcMain));
            }

            var fixture = Healthy.First(kv => path.EndsWith(kv.Key, StringComparison.Ordinal)).Value;
            return Task.FromResult(Json(fixture));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class BrokenBodyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new HttpRequestException("simulated connection reset"));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(new HttpRequestException("simulated connection reset"));

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new HttpRequestException("simulated connection reset");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
