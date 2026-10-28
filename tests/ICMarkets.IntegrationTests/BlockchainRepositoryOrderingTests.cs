using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.IntegrationTests;

// Guards the history-ordering contract: GetHistory/GetLatest must return rows
// by CreatedAt DESC (Id DESC as tiebreaker), per the task spec. Runs on an
// in-memory SQLite connection - fast, offline, no Docker - so it also proves
// SQLite orders UTC DateTimeOffset values correctly (the reason the repository
// can safely order by CreatedAt on both providers).
public sealed class BlockchainRepositoryOrderingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly BlockchainRepository _repo;

    public BlockchainRepositoryOrderingTests()
    {
        // an in-memory SQLite db lives only as long as a connection is open,
        // so hold one open for the lifetime of the test class.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        // EnsureCreated builds the schema (incl. the (Network, CreatedAt DESC)
        // index) from the live model. NoOpPublisher stubs MediatR so we don't
        // pull a mocking library in for one unused dependency.
        _db = new AppDbContext(options, new NoOpPublisher());
        _db.Database.EnsureCreated();

        _repo = new BlockchainRepository(_db);
    }

    [Fact]
    public async Task GetHistoryAsync_returns_rows_newest_CreatedAt_first()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // inserted deliberately out of chronological order
        await Add(Network.BtcMain, t0.AddMinutes(10));
        await Add(Network.BtcMain, t0.AddMinutes(30)); // newest
        await Add(Network.BtcMain, t0.AddMinutes(20));
        await _db.SaveChangesAsync(CancellationToken.None);

        var history = await _repo.GetHistoryAsync(Network.BtcMain, take: 50, skip: 0, CancellationToken.None);

        Assert.Equal(
            new[] { t0.AddMinutes(30), t0.AddMinutes(20), t0.AddMinutes(10) },
            history.Select(s => s.CreatedAt));
    }

    [Fact]
    public async Task GetHistoryAsync_filters_to_the_requested_network()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await Add(Network.BtcMain, t0.AddMinutes(1));
        await Add(Network.EthMain, t0.AddMinutes(2));
        await _db.SaveChangesAsync(CancellationToken.None);

        var history = await _repo.GetHistoryAsync(Network.BtcMain, take: 50, skip: 0, CancellationToken.None);

        Assert.Single(history);
        Assert.All(history, s => Assert.Equal(Network.BtcMain, s.Network));
    }

    [Fact]
    public async Task GetHistoryAsync_honours_skip_and_take()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            await Add(Network.BtcMain, t0.AddMinutes(i));
        await _db.SaveChangesAsync(CancellationToken.None);

        // newest-first order is [t+4, t+3, t+2, t+1, t+0]; skip 1, take 2 -> [t+3, t+2]
        var page = await _repo.GetHistoryAsync(Network.BtcMain, take: 2, skip: 1, CancellationToken.None);

        Assert.Equal(
            new[] { t0.AddMinutes(3), t0.AddMinutes(2) },
            page.Select(s => s.CreatedAt));
    }

    [Fact]
    public async Task GetLatestAsync_returns_the_max_CreatedAt_row()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await Add(Network.LtcMain, t0.AddMinutes(5));
        await Add(Network.LtcMain, t0.AddMinutes(90)); // newest
        await Add(Network.LtcMain, t0.AddMinutes(40));
        await _db.SaveChangesAsync(CancellationToken.None);

        var latest = await _repo.GetLatestAsync(Network.LtcMain, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(t0.AddMinutes(90), latest!.CreatedAt);
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_when_no_rows_for_network()
    {
        var latest = await _repo.GetLatestAsync(Network.DashMain, CancellationToken.None);

        Assert.Null(latest);
    }

    private Task Add(Network network, DateTimeOffset createdAt) =>
        _repo.AddAsync(
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
                feeRates: null,
                gasPrices: null,
                lastForkHeight: null,
                lastForkHash: null,
                rawJson: "{}",
                createdAt: createdAt),
            CancellationToken.None);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
