using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// A 429 with "Retry-After: <HTTP-date>" must delay the retry until that date. The
// standard resilience handler honours both header forms by itself; a custom
// DelayGenerator that read only Retry-After.Delta (null for the date form) silently
// fell back to the ~2s exponential backoff and retried far too early. This runs the
// REAL client registration with its resilience handler and measures the gap between
// the two attempts at the primary-handler level (the point where requests leave the
// pipeline); only the first /btc/main call is answered with the 429 locally, every
// other request goes to the real WireMock server.
public sealed class RetryAfterTests : IClassFixture<RetryAfterApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RetryAfterApiFactory _factory;
    private readonly HttpClient _client;

    public RetryAfterTests(RetryAfterApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upstream429WithHttpDateRetryAfter_RetriesNoEarlierThanTheDate()
    {
        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Failed.Should().BeEmpty("the retry after the 429 must succeed");
        result.Succeeded.Should().HaveCount(5);

        var attempts = _factory.BtcAttempts.OrderBy(t => t).ToList();
        attempts.Should().HaveCount(2, "one 429 and one successful retry");

        // HTTP-date has one-second resolution: the header allows a retry no sooner than
        // ~5s after the first attempt; the default backoff (~2s) would be far earlier.
        (attempts[1] - attempts[0]).Should().BeGreaterThan(TimeSpan.FromSeconds(4),
            "the retry must wait for the Retry-After date, not the ~2s default backoff");
    }
}

public sealed class RetryAfterApiFactory : ApiFactory
{
    private readonly RateLimitFirstBtcCallHandler _handler = new();

    public IReadOnlyCollection<DateTimeOffset> BtcAttempts => _handler.Attempts.ToArray();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.AddHttpClient<IChainInfoSource, BlockCypherClient>()
                .ConfigurePrimaryHttpMessageHandler(() => _handler));
    }

    private sealed class RateLimitFirstBtcCallHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(new SocketsHttpHandler());
        private int _btcCalls;

        public ConcurrentQueue<DateTimeOffset> Attempts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/btc/main", StringComparison.Ordinal))
            {
                Attempts.Enqueue(DateTimeOffset.UtcNow);

                if (Interlocked.Increment(ref _btcCalls) == 1)
                {
                    var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    // sent as an HTTP-date (RFC 1123), not delta-seconds
                    tooMany.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(6));
                    return Task.FromResult(tooMany);
                }
            }

            return _inner.SendAsync(request, ct);
        }
    }
}
