using System.Text;
using AwesomeAssertions;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ICMarkets.IntegrationTests;

// What "PublishAsync completed" really guarantees, against a real broker:
// confirmed + persistent + routed, a stable MessageId, and - the part the outbox
// relies on - a loud failure (not a silent drop) when nothing can receive it.
[Collection(RabbitMqCollection.Name)]
public sealed class RabbitMqEventPublisherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly RabbitMqContainerFixture _fixture;

    public RabbitMqEventPublisherTests(RabbitMqContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Publish_ToABoundQueue_IsConfirmed_Persistent_AndCarriesTheMessageId()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        var options = NewOptions(queue: $"it-queue-{Guid.NewGuid():N}");
        await using var publisher = new RabbitMqEventPublisher(Options.Create(options));
        using var cts = new CancellationTokenSource(Timeout);

        await publisher.PublishAsync("42", "SnapshotIngested", "{\"height\":1}", cts.Token);

        // read it back over an independent connection
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
        };
        await using var connection = await factory.CreateConnectionAsync(cts.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cts.Token);

        var message = await channel.BasicGetAsync(options.Queue, autoAck: true, cts.Token);

        message.Should().NotBeNull("a confirmed publish to a bound queue must be retrievable");
        message!.BasicProperties.MessageId.Should().Be("42");
        message.BasicProperties.DeliveryMode.Should().Be(DeliveryModes.Persistent);
        Encoding.UTF8.GetString(message.Body.ToArray()).Should().Be("{\"height\":1}");
    }

    [SkippableFact]
    public async Task Publish_WithNoQueueBound_Throws_InsteadOfSilentlyDroppingTheMessage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        // Queue empty -> only the exchange exists, nothing is bound to it, so the
        // mandatory publish is unroutable. The outbox must see an exception (and
        // keep the row), not a successful return.
        var options = NewOptions(queue: string.Empty);
        await using var publisher = new RabbitMqEventPublisher(Options.Create(options));
        using var cts = new CancellationTokenSource(Timeout);

        var publish = () => publisher.PublishAsync("7", "SnapshotIngested", "{}", cts.Token);

        await publish.Should().ThrowAsync<Exception>();
    }

    private OutboxOptions NewOptions(string queue) => new()
    {
        HostName = _fixture.HostName,
        Port = _fixture.Port,
        UserName = RabbitMqContainerFixture.UserName,
        Password = RabbitMqContainerFixture.Password,
        Exchange = $"it-exchange-{Guid.NewGuid():N}",
        Queue = queue,
    };
}

// No container needed: a broker that is not there must make PublishAsync throw
// (so the outbox row stays unprocessed and is retried), never hang or "succeed".
public sealed class RabbitMqBrokerDownTests
{
    [Fact]
    public async Task Publish_WhenTheBrokerIsUnreachable_Throws()
    {
        // port 1 on loopback: nothing listens, the connection is refused at once.
        var options = new OutboxOptions { HostName = "127.0.0.1", Port = 1 };
        await using var publisher = new RabbitMqEventPublisher(Options.Create(options));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var publish = () => publisher.PublishAsync("1", "SnapshotIngested", "{}", cts.Token);

        await publish.Should().ThrowAsync<Exception>();
    }
}
