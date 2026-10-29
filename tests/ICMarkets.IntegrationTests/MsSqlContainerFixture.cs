using Testcontainers.MsSql;

namespace ICMarkets.IntegrationTests;

// One SQL Server container shared across the whole assembly (see
// MsSqlCollection) rather than one per test class - starting the container
// is the expensive part, and the tests below isolate themselves with their
// own per-test database instead.
//
// shortcut: Docker-unavailable is detected by catching the start failure,
// not by probing the Docker daemon first - simpler, and the failure mode
// (image pull / daemon connect error) is the same either way. Every test
// that depends on this fixture checks IsAvailable and returns early rather
// than failing, so `dotnet test` stays green on a machine with no Docker.
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            // pin the image explicitly - MsSqlBuilder()'s parameterless ctor is
            // obsolete in current Testcontainers.MsSql; same image as the
            // docker-compose `db` service, already known to work in this setup.
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MsSqlContainer";
}
