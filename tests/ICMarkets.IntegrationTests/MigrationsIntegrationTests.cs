using ICMarkets.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICMarkets.IntegrationTests;

// Guards that the committed SQL Server migrations (src/ICMarkets.Infrastructure/Migrations)
// actually apply cleanly to a brand-new database - the same thing Program.cs
// does on startup (db.Database.MigrateAsync()) outside the SQLite fallback path.
[Collection(MsSqlCollection.Name)]
public sealed class MigrationsIntegrationTests
{
    private readonly MsSqlContainerFixture _fixture;

    public MigrationsIntegrationTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task MigrateAsync_OnAFreshDatabase_AppliesAllMigrationsCleanly()
    {
        if (!_fixture.IsAvailable) return;

        var builder = new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            InitialCatalog = $"icmarkets_migrations_{Guid.NewGuid():N}",
        };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        await using var db = new AppDbContext(options, new NoOpPublisher());

        await db.Database.MigrateAsync();

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        // schema is usable immediately after migrating, not just "created".
        var count = await db.Snapshots.CountAsync();
        Assert.Equal(0, count);

        await db.Database.EnsureDeletedAsync();
    }
}
