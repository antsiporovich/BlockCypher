using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ICMarkets.Infrastructure.Messaging;

// Single shared connection/channel, created lazily on first publish and
// recreated if it's no longer open - fine for this workload (a handful of
// SnapshotIngested events per ingestion run, not a high-throughput producer).
// A fanout exchange means any future consumer (another service, a second
// queue for analytics, etc.) can bind its own queue without this publisher
// changing - that's the whole point of "event-driven", not just "a queue".
//
// What "published" means here (see IEventPublisher for the caller contract):
//  - publisher confirms are ON and tracked: BasicPublishAsync only completes
//    once the broker has acked the message, and throws on a nack. Completing
//    the call is NOT merely "bytes handed to the socket".
//  - mandatory = true: a message the exchange cannot route to any queue is
//    returned by the broker and surfaces as an exception, instead of being
//    silently dropped while the outbox row is already marked processed.
//  - Persistent = true + a durable queue: the message survives a broker restart.
//  - MessageId = the outbox row id: stable across retries, so consumers can
//    de-duplicate (at-least-once, not exactly-once).
public sealed class RabbitMqEventPublisher(IOptions<OutboxOptions> options)
    : IEventPublisher, IAsyncDisposable
{
    private readonly OutboxOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(string messageId, string type, string payload, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelAsync(ct);

            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = messageId,
                Type = type,
                ContentType = "application/json",
            };

            // With confirmation tracking enabled this awaits the broker's ack and
            // throws PublishException on a nack or an unroutable (mandatory) return.
            await channel.BasicPublishAsync(
                _options.Exchange,
                routingKey: type,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(payload),
                cancellationToken: ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await CloseQuietlyAsync();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
        };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        await _channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);

        // A fanout exchange with no bound queue drops everything, so for the demo
        // declare a durable queue and bind it - published events then have
        // somewhere to land and are visible in the management UI. Real consumers
        // declare their own queues; set Outbox:Queue to empty to declare nothing
        // (then, with no binding, mandatory publishes are returned and the outbox
        // rows stay unprocessed until a consumer binds).
        if (!string.IsNullOrWhiteSpace(_options.Queue))
        {
            // bounded on purpose: nothing consumes the demo queue, so cap its age
            // and length rather than letting it grow forever.
            var arguments = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = (long)TimeSpan.FromHours(24).TotalMilliseconds,
                ["x-max-length"] = 10_000,
            };

            await _channel.QueueDeclareAsync(_options.Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);
            await _channel.QueueBindAsync(_options.Queue, _options.Exchange, routingKey: string.Empty, cancellationToken: ct);
        }

        return _channel;
    }

    private async Task CloseQuietlyAsync()
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* already broken */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* already broken */ }
        _channel = null;
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseQuietlyAsync();
        _lock.Dispose();
    }
}
