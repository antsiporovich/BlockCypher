using ICMarkets.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ICMarkets.Infrastructure.Persistence;

// Builds the "fresh unit of work" the Application layer asks for: a new DI scope, hence a
// new AppDbContext + repository, released when the unit of work is disposed. Scope and
// DbContext lifetime management lives here, in the composition-side layer, not in Application.
public sealed class IngestionUnitOfWorkFactory(IServiceScopeFactory scopeFactory) : IUnitOfWorkFactory
{
    public IIngestionUnitOfWork Create()
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            return new IngestionUnitOfWork(
                scope,
                scope.ServiceProvider.GetRequiredService<IBlockchainRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class IngestionUnitOfWork(AsyncServiceScope scope, IBlockchainRepository snapshots, IUnitOfWork unitOfWork)
        : IIngestionUnitOfWork
    {
        public IBlockchainRepository Snapshots => snapshots;

        public Task<int> SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
