using AwesomeAssertions;
using ICMarkets.Application.Features.History;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.IntegrationTests;

// Exercises BlockchainRepository + AppDbContext (IUnitOfWork) against a real
// SQL Server instance - the provider the app actually ships on (see
// docs/DECISIONS.md #9: testing only against SQLite would let real SQL
// Server-only bugs through). Each test gets its own database (via a unique
// catalog name) inside the one shared container, so tests never see each
// other's rows and can run in any order.
[Collection(MsSqlCollection.Name)]
public sealed class BlockchainRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private AppDbContext? _db;
    private BlockchainRepository? _repo;

    public BlockchainRepositoryIntegrationTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
            return;

        var builder = new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            InitialCatalog = $"icmarkets_it_{Guid.NewGuid():N}",
        };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        _db = new AppDbContext(options, new NoOpPublisher());
        await _db.Database.MigrateAsync();
        _repo = new BlockchainRepository(_db);
    }

    public async Task DisposeAsync()
    {
        if (_db is null)
            return;

        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task AddAsync_ThenSaveChanges_PersistsTheSnapshot_AndRoundTripsRawJsonAndNullableColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = BlockchainSnapshot.Create(
            network: Network.BtcMain,
            name: "BTC.main",
            height: 973576,
            hash: "hash-1",
            time: createdAt,
            latestUrl: "https://example/latest",
            previousHash: "prev-hash",
            previousUrl: "https://example/previous",
            peerCount: 324,
            unconfirmedCount: 7749,
            feeRates: new FeeRates(3109, 1646, 1378),
            gasPrices: null,
            lastForkHeight: 969888,
            lastForkHash: "fork-hash",
            rawJson: "{\"height\":973576}",
            createdAt: createdAt);

        await _repo!.AddAsync(snapshot, CancellationToken.None);
        await _db!.SaveChangesAsync(CancellationToken.None);

        var reloaded = await _repo.GetLatestAsync(Network.BtcMain, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.RawJson.Should().Be("{\"height\":973576}");
        reloaded.FeeRates.Should().NotBeNull();
        reloaded.FeeRates!.High.Should().Be(3109);
        reloaded.GasPrices.Should().BeNull();
        reloaded.LastForkHeight.Should().Be(969888);
    }

    [SkippableFact]
    public async Task GetHistoryAsync_OrdersByCreatedAtDescending()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await AddSnapshot(Network.EthMain, t0.AddMinutes(10));
        await AddSnapshot(Network.EthMain, t0.AddMinutes(30)); // newest
        await AddSnapshot(Network.EthMain, t0.AddMinutes(20));
        await _db!.SaveChangesAsync(CancellationToken.None);

        var history = await _repo!.GetHistoryAsync(Network.EthMain, take: 50, after: null, CancellationToken.None);

        history.Select(s => s.CreatedAt).Should().Equal(t0.AddMinutes(30), t0.AddMinutes(20), t0.AddMinutes(10));
    }

    [SkippableFact]
    public async Task GetHistoryAsync_KeysetPaginates_StrictlyAfterTheCursor()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            await AddSnapshot(Network.LtcMain, t0.AddMinutes(i));
        await _db!.SaveChangesAsync(CancellationToken.None);

        // newest-first order is [t+4, t+3, t+2, t+1, t+0]. First page of two fetches take + 1 = 3
        // as the has-next probe; its last requested row (t+3) becomes the cursor.
        var first = await _repo!.GetHistoryAsync(Network.LtcMain, take: 2, after: null, CancellationToken.None);
        first.Should().HaveCount(3);
        first.Take(2).Select(s => s.CreatedAt).Should().Equal(t0.AddMinutes(4), t0.AddMinutes(3));

        var cursor = new HistoryCursor(first[1].CreatedAt, first[1].Id);
        var page = await _repo.GetHistoryAsync(Network.LtcMain, take: 2, after: cursor, CancellationToken.None);

        // strictly after t+3 -> [t+2, t+1]
        page.Take(2).Select(s => s.CreatedAt).Should().Equal(t0.AddMinutes(2), t0.AddMinutes(1));
    }

    [SkippableFact]
    public async Task GetLatestAsync_ReturnsTheMostRecentRow_PerNetwork()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await AddSnapshot(Network.BtcMain, t0.AddMinutes(5));
        await AddSnapshot(Network.EthMain, t0.AddMinutes(999)); // different network, must not win
        await AddSnapshot(Network.BtcMain, t0.AddMinutes(50)); // newest for BtcMain
        await _db!.SaveChangesAsync(CancellationToken.None);

        var latest = await _repo!.GetLatestAsync(Network.BtcMain, CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.CreatedAt.Should().Be(t0.AddMinutes(50));
        latest.Network.Should().Be(Network.BtcMain);
    }

    [SkippableFact]
    public async Task GetLatestAsync_ReturnsNull_WhenNoRowsForThatNetwork()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var latest = await _repo!.GetLatestAsync(Network.DashMain, CancellationToken.None);

        latest.Should().BeNull();
    }

    private Task AddSnapshot(Network network, DateTimeOffset createdAt) =>
        _repo!.AddAsync(
            BlockchainSnapshot.Create(
                network: network,
                name: network.ToString(),
                height: 1,
                hash: "hash",
                time: createdAt,
                latestUrl: null,
                previousHash: null,
                previousUrl: null,
                peerCount: 0,
                unconfirmedCount: 0,
                feeRates: network == Network.EthMain ? null : new FeeRates(1, 1, 1),
                gasPrices: network == Network.EthMain ? new GasPrices(1, 1, 1, null, null, null, null) : null,
                lastForkHeight: null,
                lastForkHash: null,
                rawJson: "{}",
                createdAt: createdAt),
            CancellationToken.None);
}
