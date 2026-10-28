namespace ICMarkets.Application.Abstractions;

// Separates "stage a change" (repository) from "commit it" (unit of work),
// so a handler can batch several repository calls into one SaveChanges.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
