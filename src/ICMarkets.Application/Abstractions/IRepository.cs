namespace ICMarkets.Application.Abstractions;

// Generic base - this project only has one aggregate today, but the base
// keeps the Repository pattern visible and gives a future aggregate a seam
// without duplicating AddAsync's shape.
public interface IRepository<TEntity> where TEntity : class
{
    Task AddAsync(TEntity entity, CancellationToken ct);
}
