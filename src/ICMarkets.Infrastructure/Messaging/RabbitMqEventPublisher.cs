using System.Text;
using ICMarkets.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ICMarkets.Infrastructure.Messaging;

// Single shared connection/channel, created lazily on first publish and
// recreated if it's no longer open - fine for this workload (a handful of
// SnapshotIngested events per ingestion run, not a high-throughput producer).
// A fanout exchange means any future consumer (another service, a second
// queue for analytics, etc.) can bind its own queue without this publisher
// changing - that's the whole point of "event-driven", not just "a queue".
public sealed class RabbitMqEventPublisher(IOptions<OutboxOptions> options)
    : IEventPublisher, IAsyncDisposable
{
    private readonly OutboxOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(string type, string payload, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelAsync(ct);
            await channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
            await channel.BasicPublishAsync(
                _options.Exchange,
                routingKey: type,
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

        var factory = new ConnectionFactory { HostName = _options.HostName, Port = _options.Port };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.CloseAsync();
        if (_connection is not null) await _connection.CloseAsync();
    }
}
