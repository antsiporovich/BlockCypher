using ICMarkets.Infrastructure;
using ICMarkets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));

app.UseHttpsRedirection();
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
