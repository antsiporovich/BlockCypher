using Testcontainers.RabbitMq;

namespace ICMarkets.IntegrationTests;

// One RabbitMQ container shared across the RabbitMQ tests. Same two policies as
// MsSqlContainerFixture: locally, no Docker -> dependent tests are SKIPPED with a
// reason; with REQUIRE_RABBITMQ=1 (mandatory CI) a start failure is rethrown so
// the tests FAIL instead of silently not running.
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    public const string RequireEnvVar = "REQUIRE_RABBITMQ";

    // explicit non-guest account: RabbitMQ's built-in guest user may only connect from
    // loopback inside the container, which a Docker-mapped port is not.
    public const string UserName = "it-user";
    public const string Password = "it-password";

    private RabbitMqContainer? _container;

    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "RabbitMQ container has not been started.";
    public string HostName { get; private set; } = string.Empty;
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            // same image as the docker-compose `rabbitmq` service.
            _container = new RabbitMqBuilder("rabbitmq:4.2.7-management")
                .WithUsername(UserName)
                .WithPassword(Password)
                .Build();
            await _container.StartAsync();
            HostName = _container.Hostname;
            Port = _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"RabbitMQ container unavailable (is Docker running?): {ex.GetType().Name}: {ex.Message}";

            if (Environment.GetEnvironmentVariable(RequireEnvVar) is "1" or "true" or "TRUE" or "True")
                throw new InvalidOperationException(
                    $"{RequireEnvVar} is set but the RabbitMQ test container could not be started. " +
                    "Integration tests must run, not skip, in this environment.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "RabbitMqContainer";
}
