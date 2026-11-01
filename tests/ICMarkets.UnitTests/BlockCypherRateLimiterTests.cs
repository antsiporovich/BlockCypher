using System.Collections.Concurrent;
using AwesomeAssertions;
using ICMarkets.Domain;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ICMarkets.UnitTests.BlockCypher;

// The limiter contract, proven deterministically on a fake clock: at most N grants in any
// one-second window - including right after an idle period (the case a token bucket gets
// wrong: a token spent just before a refill tick lets the next grant through almost
// immediately).
public sealed class BlockCypherRateLimiterTests
{
    // The fake clock is advanced in 5 ms steps and a grant is stamped when its continuation
    // happens to run - on a loaded CI runner that can be several steps late. Only the two end
    // timestamps of a measured span carry that skew, so the assertions allow Skew on each side
    // (20 ms, with margin over what CI showed: ~8 ms). A token bucket misses by ~100+ ms.
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan Skew = 4 * Step;
    private static readonly TimeSpan Interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 3);

    private static BlockCypherRateLimiter Create(int perSecond, FakeTimeProvider clock) =>
        new(Options.Create(new BlockCypherOptions
        {
            BaseUrl = "http://localhost",
            Networks = new Dictionary<Network, string>(),
            RequestsPerSecond = perSecond,
        }), clock);

    // Drives the fake clock forward until `count` waiters have been granted.
    private static async Task<List<DateTimeOffset>> RunAsync(FakeTimeProvider clock, BlockCypherRateLimiter limiter, int count)
    {
        var grants = new ConcurrentQueue<DateTimeOffset>();
        var waiters = Enumerable.Range(0, count).Select(async _ =>
        {
            await limiter.WaitAsync(CancellationToken.None);
            grants.Enqueue(clock.GetUtcNow());
        }).ToArray();

        var guard = 0;
        while (grants.Count < count && guard++ < 5000)
        {
            clock.Advance(Step);
            await Task.Delay(1);
        }

        await Task.WhenAll(waiters);
        return grants.OrderBy(g => g).ToList();
    }

    [Fact]
    public async Task ManyWaiters_NeverGetMoreThanNInAnyOneSecondWindow()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = Create(3, clock);

        var grants = await RunAsync(clock, limiter, 12);

        var window = TimeSpan.FromSeconds(1) - 2 * Skew;
        var busiest = grants.Max(start => grants.Count(g => g >= start && g < start + window));
        busiest.Should().BeLessThanOrEqualTo(3);
        // and consecutive grants are at least 1/3 s apart.
        for (var i = 1; i < grants.Count; i++)
            (grants[i] - grants[i - 1]).Should().BeGreaterThanOrEqualTo(Interval - 2 * Skew);
    }

    [Fact]
    public async Task AfterAnIdlePeriod_TheFirstTwoGrantsAreStillOneIntervalApart()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = Create(3, clock);

        // one grant, then a long idle stretch (a token bucket would be full again).
        await RunAsync(clock, limiter, 1);
        clock.Advance(TimeSpan.FromSeconds(30));

        // a burst of three arrives at once: must not be released together.
        var grants = await RunAsync(clock, limiter, 3);

        (grants[1] - grants[0]).Should().BeGreaterThanOrEqualTo(Interval - 2 * Skew);
        (grants[2] - grants[1]).Should().BeGreaterThanOrEqualTo(Interval - 2 * Skew);
    }

    [Fact]
    public async Task ACancelledWaiter_DoesNotBlockTheOnesBehindIt()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = Create(3, clock);
        await RunAsync(clock, limiter, 1);

        using var cts = new CancellationTokenSource();
        var cancelled = limiter.WaitAsync(cts.Token).AsTask();
        var next = limiter.WaitAsync(CancellationToken.None).AsTask();
        cts.Cancel();

        await ((Func<Task>)(() => cancelled)).Should().ThrowAsync<OperationCanceledException>();

        var guard = 0;
        while (!next.IsCompleted && guard++ < 1000)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Delay(1);
        }

        next.IsCompletedSuccessfully.Should().BeTrue();
    }
}
