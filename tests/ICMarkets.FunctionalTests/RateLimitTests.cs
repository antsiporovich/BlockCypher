using System.Collections.Concurrent;
using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Domain;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// "3 requests in flight" is not "3 requests per second", and one batch's cap says
// nothing about several callers at once. Here four independent callers (think: four
// concurrent POST /ingest calls) each fetch all five networks through the REAL client
// registration - 20 requests competing for one budget. The recording handler sits at
// the very end of the pipeline (after the rate limiter), so it sees exactly what would
// reach BlockCypher. No database is involved, so the callers can truly run in parallel.
// Runs alone inside this assembly (DisableParallelization): the arrival timestamps are
// taken after a permit is granted and picked up by whatever thread is free, so other
// busy tests would add scheduler jitter to a timing measurement.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RateLimitCollection : ICollectionFixture<RateLimitApiFactory>
{
    public const string Name = "RateLimit";
}

[Collection(RateLimitCollection.Name)]
public sealed class RateLimitTests
{
    private readonly RateLimitApiFactory _factory;

    public RateLimitTests(RateLimitApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ManyConcurrentCallers_NeverExceedTheConfiguredRequestsPerSecond()
    {
        const int callers = 4;

        var work = Enumerable.Range(0, callers).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IBlockCypherClient>();
            await Task.WhenAll(Enum.GetValues<Network>().Select(n => client.GetChainInfoAsync(n, CancellationToken.None)));
        });
        await Task.WhenAll(work);

        var stamps = _factory.Attempts;
        stamps.Should().HaveCount(callers * Enum.GetValues<Network>().Length);

        // busiest one-second window. The limiter spaces permits ~333 ms apart (3 per
        // second); the tolerance covers timestamp jitter - dispatch can be delayed and
        // bunched by the scheduler, e.g. while another test process (Docker containers)
        // is loading the machine. Without a limiter all 20 arrive within ~100 ms.
        var busiest = stamps.Max(start => stamps.Count(t => t >= start && t < start + TimeSpan.FromSeconds(1)));
        var offsets = string.Join(", ", stamps.Select(t => (int)(t - stamps[0]).TotalMilliseconds));
        busiest.Should().BeLessThanOrEqualTo(5,
            $"at most ~3 requests per second may reach the upstream; arrival offsets in ms: [{offsets}]");

        // and it really did throttle: 20 requests at 3/s cannot finish in a blink.
        (stamps[^1] - stamps[0]).Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }
}

public sealed class RateLimitApiFactory : ApiFactory
{
    private readonly RecordingHandler _handler = new();

    public IReadOnlyList<DateTimeOffset> Attempts => _handler.Stamps.OrderBy(t => t).ToList();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlockCypher:RequestsPerSecond"] = "3",
        }));

        builder.ConfigureServices(services =>
            services.AddHttpClient<IBlockCypherClient, BlockCypherClient>()
                .ConfigurePrimaryHttpMessageHandler(() => _handler));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(new SocketsHttpHandler());

        public ConcurrentQueue<DateTimeOffset> Stamps { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Stamps.Enqueue(DateTimeOffset.UtcNow);
            return _inner.SendAsync(request, ct);
        }
    }
}
