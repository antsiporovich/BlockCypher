using ICMarkets.Domain.Events;
using MediatR;

namespace ICMarkets.Application.Notifications;

// MediatR wrapper around the domain event - keeps Domain free of the MediatR
// dependency while still giving the event a pub/sub home.
public sealed record SnapshotIngestedNotification(SnapshotIngested Event) : INotification;
