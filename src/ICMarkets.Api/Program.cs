using System.Text.Json.Serialization;
using ICMarkets.Api.Infrastructure;
using ICMarkets.Application;
using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default MEL console logger; ReadFrom.Configuration lets a
// "Serilog" appsettings section (sinks, levels, enrichers - e.g. the Elastic Common
// Schema formatter in a later phase) take over without another code change.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

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

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));

app.UseHttpsRedirection();
app.UseCors("Default");
app.MapControllers();
app.MapHealthChecks("/health");

// migrations on SQL Server, EnsureCreated on the SQLite no-Docker path
// (documented compromise - see docs/DECISIONS.md)
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
