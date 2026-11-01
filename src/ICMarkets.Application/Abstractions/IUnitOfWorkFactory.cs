namespace ICMarkets.Application.Abstractions;

// Port for "give me a fresh, isolated unit of work". The ingestion pipeline persists
// each fetched network in its OWN unit of work: a failed SaveChanges leaves the rejected
// entity tracked as Added, so a shared context would retry inserting it together with the
// next valid one. How "fresh" is achieved (a DI scope + DbContext) is an Infrastructure
// concern - Application never sees the container.
public interface IUnitOfWorkFactory
{
    IIngestionUnitOfWork Create();
}

/// <summary>One short-lived persistence unit. Dispose it (even after a failure) to discard its state.</summary>
public interface IIngestionUnitOfWork : IAsyncDisposable
{
    IBlockchainRepository Snapshots { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
}
