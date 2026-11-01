using ICMarkets.Domain;
using ICMarkets.Infrastructure.BlockCypher;
using Microsoft.Extensions.Options;

namespace ICMarkets.Infrastructure;

// Fail-fast configuration. `required` only proves the binder set a value, not that it is
// usable: a relative BaseUrl or BatchSize = 0 used to start the app, report ready, and only
// fail at first use (HttpClient creation, or a silent .Take(0)). These run at startup via
// ValidateOnStart. Config only - no BlockCypher call is made.

internal sealed class BlockCypherOptionsValidator : IValidateOptions<BlockCypherOptions>
{
    public ValidateOptionsResult Validate(string? name, BlockCypherOptions o)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            errors.Add($"BlockCypher:BaseUrl must be an absolute http(s) URI, but was '{o.BaseUrl}'.");

        foreach (var network in Enum.GetValues<Network>())
            if (o.Networks is null || !o.Networks.TryGetValue(network, out var path) || string.IsNullOrWhiteSpace(path))
                errors.Add($"BlockCypher:Networks must define a non-empty path for '{network}'.");

        if (o.FetchTimeoutSeconds <= 0)
            errors.Add("BlockCypher:FetchTimeoutSeconds must be greater than 0.");
        if (o.RequestsPerSecond <= 0)
            errors.Add("BlockCypher:RequestsPerSecond must be greater than 0.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions o) =>
        o.IntervalSeconds > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Ingestion:IntervalSeconds must be greater than 0.");
}

internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions o)
    {
        if (!o.Enabled)
            return ValidateOptionsResult.Success; // off: nothing is used, nothing to validate

        var errors = new List<string>();

        if (o.BatchSize <= 0) errors.Add("Outbox:BatchSize must be greater than 0 (0 would silently select no rows).");
        if (o.PollIntervalSeconds <= 0) errors.Add("Outbox:PollIntervalSeconds must be greater than 0.");
        if (o.PublishTimeoutSeconds <= 0) errors.Add("Outbox:PublishTimeoutSeconds must be greater than 0.");
        if (o.RetentionHours <= 0) errors.Add("Outbox:RetentionHours must be greater than 0.");
        if (string.IsNullOrWhiteSpace(o.HostName)) errors.Add("Outbox:HostName must not be empty.");
        if (o.Port is <= 0 or > 65535) errors.Add("Outbox:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(o.Exchange)) errors.Add("Outbox:Exchange must not be empty.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
