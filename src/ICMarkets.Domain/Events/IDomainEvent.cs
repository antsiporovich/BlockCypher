namespace ICMarkets.Domain.Events;

// zero-dependency marker - kept separate from MediatR's INotification so
// Domain stays dependency-free. Infrastructure adapts these to MediatR
// notifications at the dispatch point (AppDbContext.SaveChangesAsync).
public interface IDomainEvent;
