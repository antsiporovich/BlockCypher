using System.Text.Json.Serialization;
using Elastic.CommonSchema.Serilog;
using ICMarkets.Api.Infrastructure;
using ICMarkets.Application;
using ICMarkets.Application.Telemetry;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Enrichers.Span;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default MEL console logger. The Elastic Common Schema
// formatter (~15 min to wire) makes the console output Kibana-ingestible by
// construction - a named requirement in the posting - without standing up an
// ELK stack for an assessment. Enrich.WithSpan() adds the active trace/span
// id to every log line so a log can be correlated back to the OTel trace
// that produced it.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console(new EcsTextFormatter()));

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        // PropertyNamingPolicy is already camelCase by default for ASP.NET Core's
        // System.Text.Json integration - listed here to make the choice explicit.
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

// OTLP export is opt-in via config/env (OTEL_EXPORTER_OTLP_ENDPOINT or
// OpenTelemetry:OtlpEndpoint) - without a collector configured, traces still
// flow through the pipeline (visible to e.g. an in-process listener or a
// future exporter) but nothing attempts a network call to a backend that
// doesn't exist in this compose stack.
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ICMarkets.BlockCypherApi"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(IngestionTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics => metrics
        .AddMeter(IngestionTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // /metrics below - scraped by the Prometheus service in docker-compose.
        .AddPrometheusExporter());

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Default", policy =>
{
    // No origins configured (e.g. local dev / this assessment) -> allow any
    // origin, since there's no browser-based client to scope this to yet.
    // A real deployment sets Cors:AllowedOrigins and gets a locked-down policy.
    if (corsOrigins.Length == 0)
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Honor X-Forwarded-* from the optional YARP Gateway (src/ICMarkets.Gateway)
// so HttpContext.Request.Host reflects the gateway's public host:port
// (localhost:8081) instead of the internal Docker DNS name YARP otherwise
// proxies with (api:8080) - without this, the native OpenAPI document's
// auto-detected server URL bakes in "api:8080", which Swagger UI's "Execute"
// can't reach from a host browser. Opt-in via the ForwardedHeaders config section
// and honored ONLY from the configured networks/proxies (docker-compose lists the
// compose network); an unconfigured API trusts no X-Forwarded-* at all.
var forwardedHeaders = ForwardedHeadersSetup.Build(app.Configuration);
if (forwardedHeaders is not null)
    app.UseForwardedHeaders(forwardedHeaders);

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));

// shortcut: no UseHttpsRedirection() - the container only listens on
// HTTP:8080 (see docker-compose/Dockerfile), there's no HTTPS endpoint to
// redirect to, and keeping the call just logs an ASP0017 warning on every
// startup for nothing.
app.UseCors("Default");
app.MapControllers();
app.MapHealthChecks("/health"); // everything, incl. DB (docker-compose healthcheck, humans)
// k8s split: liveness must NOT touch the DB, or a DB outage makes the kubelet
// restart a perfectly healthy API; readiness DOES, so traffic stops while the
// DB is unreachable but the pod stays up.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapPrometheusScrapingEndpoint(); // GET /metrics

// migrations on SQL Server, EnsureCreated on the SQLite no-Docker path
// (see ADR 0002 for why)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var provider = app.Configuration["Database:Provider"] ?? "SqlServer";
    if (provider == "Sqlite")
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
