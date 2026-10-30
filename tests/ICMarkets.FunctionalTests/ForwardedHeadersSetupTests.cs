using System.Net;
using AwesomeAssertions;
using ICMarkets.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace ICMarkets.FunctionalTests;

// X-Forwarded-* must be honored only from configured proxies; "trust everyone" (the
// previous cleared lists) lets any direct caller spoof client address, scheme and host.
public sealed class ForwardedHeadersSetupTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void NotConfigured_ForwardedHeadersAreOff()
    {
        ForwardedHeadersSetup.Build(Config()).Should().BeNull();
    }

    [Fact]
    public void EnabledWithoutAnyList_TrustsOnlyTheFrameworkDefault_NotThePrivateNetwork()
    {
        var options = ForwardedHeadersSetup.Build(Config(("ForwardedHeaders:Enabled", "true")));

        options.Should().NotBeNull();
        options!.KnownIPNetworks.Should().NotContain(n => n.Contains(IPAddress.Parse("10.1.2.3")));
        options.KnownProxies.Should().NotContain(IPAddress.Parse("10.1.2.3"));
    }

    [Fact]
    public void ConfiguredNetworksAndProxies_AreTrusted_AndNothingElse()
    {
        var options = ForwardedHeadersSetup.Build(Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12"),
            ("ForwardedHeaders:KnownProxies:0", "203.0.113.7")));

        options!.KnownIPNetworks.Should().Contain(n => n.Contains(IPAddress.Parse("172.20.0.5")));
        options.KnownIPNetworks.Should().NotContain(n => n.Contains(IPAddress.Parse("8.8.8.8")));
        options.KnownProxies.Should().Contain(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public void AnInvalidNetwork_FailsAtStartup_InsteadOfSilentlyTrustingEverything()
    {
        var build = () => ForwardedHeadersSetup.Build(Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownNetworks:0", "not-a-cidr")));

        build.Should().Throw<FormatException>();
    }
}
