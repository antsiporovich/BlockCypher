using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace ICMarkets.Api.Infrastructure;

/// <summary>Bound from the <c>ForwardedHeaders</c> configuration section.</summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>Honor X-Forwarded-* at all. Off unless a proxy/gateway really sits in front.</summary>
    public bool Enabled { get; init; }

    /// <summary>CIDR ranges whose peers may set X-Forwarded-* (e.g. the gateway's network).</summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>Individual proxy addresses that may set X-Forwarded-*.</summary>
    public string[] KnownProxies { get; init; } = [];
}

// X-Forwarded-For/Proto/Host are only trustworthy when they come from OUR proxy: anyone
// who can reach the API directly can send them, and the API would then believe a spoofed
// client address, scheme or host. So the headers are honored only from configured
// networks/proxies. Enabled with nothing configured = the ASP.NET default of loopback
// only (safe by default); invalid CIDR/IP fails at startup rather than silently trusting.
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>The options to use, or null when forwarded headers are disabled.</summary>
    public static ForwardedHeadersOptions? Build(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SectionName).Get<ForwardedHeadersSettings>()
                       ?? new ForwardedHeadersSettings();
        if (!settings.Enabled)
            return null;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        };

        // the framework's defaults (loopback) are kept; configured entries are added to them.
        foreach (var network in settings.KnownNetworks.Where(n => !string.IsNullOrWhiteSpace(n)))
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network.Trim()));

        foreach (var proxy in settings.KnownProxies.Where(p => !string.IsNullOrWhiteSpace(p)))
            options.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));

        return options;
    }
}
