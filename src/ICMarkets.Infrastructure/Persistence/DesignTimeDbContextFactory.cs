using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICMarkets.Infrastructure.Persistence;

// Pins design-time tooling (dotnet ef migrations add/remove/script) to the
// SQL Server provider, regardless of the app's configured Database:Provider.
// Migrations are maintained for SQL Server only - the SQLite fallback uses
// EnsureCreated at runtime - so generating them under SQLite would emit a
// wrong, SQLite-typed migration and rewrite the model snapshot. EF tooling
// discovers this factory automatically and prefers it over the app host.
// No live database is contacted when adding migrations; the connection string
// only selects the provider.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=BlockCypher;TrustServerCertificate=True")
            .Options;

        return new AppDbContext(options, new NoOpPublisher());
    }

    // AppDbContext requires an IPublisher; design-time never dispatches events.
    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
