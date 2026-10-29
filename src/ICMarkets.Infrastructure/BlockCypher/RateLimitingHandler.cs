using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.BlockCypher;

// One limiter for the whole process, held as a singleton: every /ingest request and
// the background worker draw from the same budget. (A limiter created per
// HttpClient handler instance would be thrown away on handler rotation and, more
// importantly, would not be shared between callers.)
public sealed class BlockCypherRateLimiter : IDisposable
{
    public BlockCypherRateLimiter(IOptions<BlockCypherOptions> options)
    {
        // Token bucket with a bucket of ONE token, replenished every 1/N s: requests are
        // spaced ~1/N s apart instead of N-at-once-then-wait. A sliding window with
        // segments cannot promise "at most N in ANY one-second window" (permits from
        // two adjacent generations can land inside one measured second); evenly spaced
        // tokens can, and a smooth stream is what a 3 req/s upstream wants anyway.
        var perSecond = Math.Max(options.Value.RequestsPerSecond, 1);
        Limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(1000.0 / perSecond),
            // over-budget callers wait (oldest first) rather than fail; the bound only
            // protects against unbounded growth.
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 100,
            AutoReplenishment = true,
        });
    }

    public RateLimiter Limiter { get; }

    public void Dispose() => Limiter.Dispose();
}

// Takes one permit per request that actually leaves the pipeline. Registered AFTER the
// resilience handler (= inner to it), so each retry attempt takes its own permit.
public sealed class RateLimitingHandler(RateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new HttpRequestException("The BlockCypher request queue is full; the request was not sent.");

        return await base.SendAsync(request, cancellationToken);
    }
}
