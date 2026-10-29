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
        result!.Succeeded.Should().HaveCount(4);
        result.Failed.Should().ContainSingle(f => f.Network == Network.LtcMain);
    }
}

public sealed class SlowBodyApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // short budget so the test takes seconds, not the production 45s.
            ["BlockCypher:FetchTimeoutSeconds"] = "2",
        }));

        builder.ConfigureServices(services =>
            services.AddHttpClient<IBlockCypherClient, BlockCypherClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StalledLtcBodyHandler()));
    }

    // Everything goes to the real WireMock server except /ltc/main, which gets
    // 200 + headers immediately and a body stream that never produces a byte.
    private sealed class StalledLtcBodyHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(new SocketsHttpHandler());

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/ltc/main", StringComparison.Ordinal))
                return _inner.SendAsync(request, ct);

            var content = new StreamContent(new StalledStream());
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
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
