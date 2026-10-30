using AwesomeAssertions;
using ICMarkets.Application.Features.History;
using ICMarkets.Application.Mapping;
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

        var history = await _repo.GetHistoryAsync(Network.BtcMain, take: 50, after: null, CancellationToken.None);

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

        var history = await _repo.GetHistoryAsync(Network.BtcMain, take: 50, after: null, CancellationToken.None);

        Assert.Single(history);
        Assert.All(history, s => Assert.Equal(Network.BtcMain, s.Network));
    }

    [Fact]
    public async Task GetHistoryAsync_returns_take_plus_one_as_a_has_next_page_probe()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            await Add(Network.BtcMain, t0.AddMinutes(i));
        await _db.SaveChangesAsync(CancellationToken.None);

        // take: 2 asks for two rows, but the repository fetches one extra so the caller
        // can tell a next page exists without a second COUNT query.
        var page = await _repo.GetHistoryAsync(Network.BtcMain, take: 2, after: null, CancellationToken.None);

        page.Should().HaveCount(3);
        page.Select(s => s.CreatedAt).Should().Equal(t0.AddMinutes(4), t0.AddMinutes(3), t0.AddMinutes(2));
    }

    // The core keyset guarantee and the whole reason OFFSET was replaced: a row ingested between
    // two page requests (the background worker does this on every tick) must never cause a row to
    // repeat or be skipped across pages. With OFFSET `.Skip(2)` page 2 here would be [t+2, t+1] -
    // t+2 duplicated. Fails if the repository reverts to OFFSET paging.
    [Fact]
    public async Task History_keyset_does_not_duplicate_rows_when_a_row_is_inserted_between_pages()
    {
        var handler = new GetChainHistoryQueryHandler(_repo, new SnapshotMapper());
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // newest-first is [t+3, t+2, t+1]
        await Add(Network.BtcMain, t0.AddMinutes(1));
        await Add(Network.BtcMain, t0.AddMinutes(2));
        await Add(Network.BtcMain, t0.AddMinutes(3));
        await _db.SaveChangesAsync(CancellationToken.None);

        var page1 = await handler.Handle(new GetChainHistoryQuery("btc-main", Take: 2), CancellationToken.None);
        page1.Items.Select(i => i.CreatedAt).Should().Equal(t0.AddMinutes(3), t0.AddMinutes(2));
        page1.NextCursor.Should().NotBeNull();

        // a newer snapshot arrives between the two page requests
        await Add(Network.BtcMain, t0.AddMinutes(4));
        await _db.SaveChangesAsync(CancellationToken.None);

        var page2 = await handler.Handle(
            new GetChainHistoryQuery("btc-main", Take: 2, Cursor: page1.NextCursor), CancellationToken.None);

        // only the row strictly older than the cursor remains; the inserted t+4 does not leak in
        page2.Items.Select(i => i.CreatedAt).Should().Equal(t0.AddMinutes(1));
        page2.NextCursor.Should().BeNull();

        // no row appears on both pages
        page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task History_invalid_cursor_is_treated_as_the_first_page_by_the_handler_but_rejected_by_the_validator()
    {
        // The handler keeps the cursor opaque: an unparseable token is ignored rather than trusted
        // (the validator is what turns it into a 400 at the API boundary - see the validator tests).
        var handler = new GetChainHistoryQueryHandler(_repo, new SnapshotMapper());
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await Add(Network.BtcMain, t0.AddMinutes(1));
        await _db.SaveChangesAsync(CancellationToken.None);

        var page = await handler.Handle(
            new GetChainHistoryQuery("btc-main", Take: 2, Cursor: "not-a-valid-cursor"), CancellationToken.None);

        page.Items.Should().ContainSingle();
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
