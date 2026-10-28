using MediatR;
using Microsoft.Extensions.Logging;

namespace ICMarkets.Application.Notifications;

// ponytail: one log-line consumer, proving the event pipeline actually
// fires end to end (an event with zero subscribers is dead code). A real
// subscriber (outbox/RabbitMQ) is stretch-goal territory (Phase 9).
public sealed class SnapshotIngestedLogger(ILogger<SnapshotIngestedLogger> logger)
    : INotificationHandler<SnapshotIngestedNotification>
{
    public Task Handle(SnapshotIngestedNotification notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Snapshot ingested: {Network} height={Height} at {CreatedAt}",
            notification.Event.Network, notification.Event.Height, notification.Event.CreatedAt);
        return Task.CompletedTask;
    }
}
