using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure.BlockCypher;

// One limiter for the whole process, held as a singleton: every /ingest request and
// the background worker draw from the same budget. (A limiter created per
// HttpClient handler instance would be thrown away on handler rotation and, more
// importantly, would not be shared between callers.)
//
// CONTRACT: at most N grants in ANY one-second window (N = RequestsPerSecond), strictly -
// no burst. Implemented as a minimum-interval gate: consecutive grants are at least
// ceil(1s / N) apart, measured from the moment the previous grant was actually released.
// Any 1s window then holds at most N grants. A token bucket cannot promise this: it
// bounds the AVERAGE rate, but if a token is spent just before a refill tick the next
// one is granted almost immediately, so two grants can land a few ms apart.
// Waiters are served one at a time through a semaphore; the interval is re-checked after
// each wait, so a timer firing a hair early can never shorten the gap.
public sealed class BlockCypherRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _minInterval;
    private long _lastGrant;
    private bool _hasGrant;

    public BlockCypherRateLimiter(IOptions<BlockCypherOptions> options, TimeProvider clock)
    {
        _clock = clock;
        var perSecond = Math.Max(options.Value.RequestsPerSecond, 1);
        // rounded UP so that N intervals are never shorter than one second.
        _minInterval = TimeSpan.FromTicks((long)Math.Ceiling(TimeSpan.TicksPerSecond / (double)perSecond));
    }

    /// <summary>Waits (FIFO-ish, one at a time) until a request may leave, then returns.</summary>
    public async ValueTask WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_hasGrant)
            {
                TimeSpan remaining;
                while ((remaining = _minInterval - _clock.GetElapsedTime(_lastGrant)) > TimeSpan.Zero)
                    await Task.Delay(remaining, _clock, ct);
            }

            // stamped AFTER the wait: the interval is counted from the real release time.
            _lastGrant = _clock.GetTimestamp();
            _hasGrant = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

// Takes one grant per request that actually leaves the pipeline. Registered AFTER the
// resilience handler (= inner to it), so each retry attempt takes its own grant.
public sealed class RateLimitingHandler(BlockCypherRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
