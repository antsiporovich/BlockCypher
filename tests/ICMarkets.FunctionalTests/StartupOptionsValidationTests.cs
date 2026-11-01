using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ICMarkets.FunctionalTests;

// Invalid configuration must stop the host at startup (ValidateOnStart), not surface later
// as a 500 on first use or a worker that loops on a swallowed error. Config validation only.
public sealed class StartupOptionsValidationTests
{
    [Fact]
    public void RelativeBaseUrl_FailsStartup()
    {
        using var factory = new ConfiguredFactory(("BlockCypher:BaseUrl", "invalid-relative-uri"));

        Action start = () => factory.CreateClient().Dispose();

        FindValidationException(start).Failures.Should().Contain(f => f.Contains("BlockCypher:BaseUrl"));
    }

    [Fact]
    public void EnabledOutbox_WithZeroBatchSize_FailsStartup()
    {
        using var factory = new ConfiguredFactory(("Outbox:Enabled", "true"), ("Outbox:BatchSize", "0"));

        Action start = () => factory.CreateClient().Dispose();

        FindValidationException(start).Failures.Should().Contain(f => f.Contains("Outbox:BatchSize"));
    }

    [Fact]
    public void NonPositiveIngestionInterval_FailsStartup()
    {
        using var factory = new ConfiguredFactory(("Ingestion:IntervalSeconds", "0"));

        Action start = () => factory.CreateClient().Dispose();

        FindValidationException(start).Failures.Should().Contain(f => f.Contains("Ingestion:IntervalSeconds"));
    }

    [Fact]
    public void ValidConfiguration_Starts()
    {
        using var factory = new ConfiguredFactory();

        using var client = factory.CreateClient();

        client.Should().NotBeNull();
    }

    // The host may wrap the failure (TargetInvocation / Aggregate); walk to the real one.
    private static OptionsValidationException FindValidationException(Action start)
    {
        Exception? caught = null;
        try { start(); }
        catch (Exception ex) { caught = ex; }

        caught.Should().NotBeNull("invalid configuration must stop the host from starting");
        for (var ex = caught; ex is not null; ex = ex.InnerException)
            if (ex is OptionsValidationException ove)
                return ove;

        throw new Xunit.Sdk.XunitException($"Expected OptionsValidationException but got: {caught}");
    }

    private sealed class ConfiguredFactory(params (string Key, string Value)[] overrides) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // added last, so these win over the factory's defaults
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(overrides.ToDictionary(o => o.Key, o => (string?)o.Value)));
        }
    }
}
