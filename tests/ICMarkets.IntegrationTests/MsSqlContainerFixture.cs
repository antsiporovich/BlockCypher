using Testcontainers.MsSql;

namespace ICMarkets.IntegrationTests;

// One SQL Server container shared across the whole assembly (see
// MsSqlCollection) rather than one per test class - starting the container
// is the expensive part, and the tests below isolate themselves with their
// own per-test database instead.
//
// shortcut: Docker-unavailable is detected by catching the start failure,
// not by probing the Docker daemon first - simpler, and the failure mode
// (image pull / daemon connect error) is the same either way.
//
// Two policies, so a green run always means something:
//  - local (default): no Docker -> every dependent test reports SKIPPED with
//    a reason (Skip.IfNot in the test), never a silent "Passed".
//  - mandatory CI: set REQUIRE_SQLSERVER=1 -> a start failure is rethrown, the
//    fixture fails to initialise, and every dependent test FAILS. The database
//    tests cannot be quietly skipped in the gate that is supposed to run them.
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    public const string RequireEnvVar = "REQUIRE_SQLSERVER";

    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "SQL Server container has not been started.";
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
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"SQL Server container unavailable (is Docker running?): {ex.GetType().Name}: {ex.Message}";

            if (IsRequired())
                throw new InvalidOperationException(
                    $"{RequireEnvVar} is set but the SQL Server test container could not be started. " +
                    "Integration tests must run, not skip, in this environment.", ex);
        }
    }

    private static bool IsRequired() =>
        Environment.GetEnvironmentVariable(RequireEnvVar) is "1" or "true" or "TRUE" or "True";

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
