using MediatR;

namespace ICMarkets.IntegrationTests;

// AppDbContext requires an IPublisher to dispatch domain events on save.
// These tests don't exercise the notification pipeline (that's covered by
// unit tests against the handler), so a no-op stub is enough and keeps the
// integration project from needing a mocking library just for this.
internal sealed class NoOpPublisher : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}
