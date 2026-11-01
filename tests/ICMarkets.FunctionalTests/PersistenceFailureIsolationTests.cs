using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.FunctionalTests;

// What these tests prove: a failure AT THE SAVE (not at validation) of one network
// leaves the other four persisted. The five upstream responses are all VALID, so
// ChainInfoValidation lets them through and every network reaches SaveChanges; an
// EF interceptor then makes the save fail for exactly one of them. (Responses that
// are invalid are rejected earlier - see ResponseValidationTests; they never reach
// the DbContext, so they cannot test this.)
//
// The interceptor rejects any SaveChanges whose change set still CONTAINS the victim
// entity - which is what a real failed insert looks like to EF. That makes the test
// sensitive to the bug it guards against: if the consumer reused ONE DbContext for
// all items, the rejected entity would stay in the ChangeTracker as Added and every
// later save would fail too (4 networks lost instead of 0). It passes only because
// each item is persisted in its own DI scope / DbContext.
public sealed class PersistenceFailureIsolationTests : IClassFixture<SaveFailureApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly SaveFailureApiFactory _factory;

    public PersistenceFailureIsolationTests(SaveFailureApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_SaveFailsForOneNetwork_ItIsReportedFailed_AndTheOtherFourArePersisted()
    {
        // the in-memory database is shared by the tests in this class; start clean.
        using (var cleanup = _factory.Services.CreateScope())
            await cleanup.ServiceProvider.GetRequiredService<AppDbContext>().Snapshots.ExecuteDeleteAsync();

        _factory.Interceptor.Reset(); // victim = the first network that reaches SaveChanges

        var response = await _client.PostAsync("/api/v1/blockchains/ingest", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // the failure really happened at the save: the interceptor fired for the victim.
        var victim = _factory.Interceptor.Victim;
        victim.Should().NotBeNull("SaveChanges must have been reached for at least one network");
        _factory.Interceptor.Rejections.Should().BeGreaterThan(0);

        var result = await response.Content.ReadFromJsonAsync<IngestAllChainsResult>(JsonOptions);
        result!.Failed.Should().ContainSingle(f => f.Network == NetworkKey.ToKey(victim!.Value));
        result.Succeeded.Should().HaveCount(4);

        // ...and the four rows really are in the database, the victim's is not.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Snapshots.Select(s => s.Network).ToListAsync();
        persisted.Should().HaveCount(4).And.NotContain(victim!.Value);
    }

    // The mechanism behind the test above, shown directly: after a failed
    // SaveChanges the rejected entity stays in the ChangeTracker as Added, so on a
    // REUSED context the next, perfectly valid save fails as well. A fresh context
    // (what the per-item scope gives each network) is unaffected.
    [Fact]
    public async Task AfterAFailedSave_AReusedContextIsPoisoned_ButAFreshOneIsNot()
    {
        _factory.Interceptor.Reset(Network.LtcMain);

        using var poisonedScope = _factory.Services.CreateScope();
        var shared = poisonedScope.ServiceProvider.GetRequiredService<AppDbContext>();

        shared.Snapshots.Add(NewSnapshot(Network.LtcMain));
        await FluentActions.Awaiting(() => shared.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

        shared.ChangeTracker.Entries<BlockchainSnapshot>()
            .Should().ContainSingle(e => e.State == EntityState.Added, "the rejected entity stays tracked");

        shared.Snapshots.Add(NewSnapshot(Network.BtcMain)); // valid on its own
        await FluentActions.Awaiting(() => shared.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>(
            "the poisoned entity is saved together with it and fails it too");

        using var freshScope = _factory.Services.CreateScope();
        var fresh = freshScope.ServiceProvider.GetRequiredService<AppDbContext>();
        fresh.Snapshots.Add(NewSnapshot(Network.BtcMain));
        await FluentActions.Awaiting(() => fresh.SaveChangesAsync()).Should().NotThrowAsync();
    }

    private static BlockchainSnapshot NewSnapshot(Network network) =>
        BlockchainSnapshot.Create(
            network, "X.main", 1, "hash", DateTimeOffset.UtcNow,
            null, null, null, 1, 1,
            network == Network.EthMain ? null : new FeeRates(1, 1, 1),
            network == Network.EthMain ? new GasPrices(1, 1, 1, null, null, null, null) : null,
            null, null, "{}", DateTimeOffset.UtcNow);
}

public sealed class SaveFailureApiFactory : ApiFactory
{
    public FailingSaveInterceptor Interceptor { get; } = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options) =>
        options.AddInterceptors(Interceptor);
}

// Rejects any SaveChanges whose change set contains an Added snapshot for the
// victim network. With no victim preset, the first network seen becomes the victim.
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    private readonly object _gate = new();
    private int _rejections;

    public Network? Victim { get; private set; }
    public int Rejections => Volatile.Read(ref _rejections);

    public void Reset(Network? victim = null)
    {
        lock (_gate)
            Victim = victim;
        Interlocked.Exchange(ref _rejections, 0);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var added = eventData.Context!.ChangeTracker.Entries<BlockchainSnapshot>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Network)
            .ToList();

        lock (_gate)
        {
            Victim ??= added.Count > 0 ? added[0] : null;
            if (Victim is { } victim && added.Contains(victim))
            {
                Interlocked.Increment(ref _rejections);
                throw new DbUpdateException($"Injected save failure for {victim}.");
            }
        }

        return ValueTask.FromResult(result);
    }
}
