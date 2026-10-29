var builder = WebApplication.CreateBuilder(args);

// shortcut: the task names an API Gateway as its own optional item - a thin
// YARP reverse proxy in front of ICMarkets.Api, not a second copy of its
// logic. Routes/clusters are entirely config-driven (appsettings.json /
// ReverseProxy__* env vars), so pointing this at a different backend, or
// adding a second one, is a config change, not a code change.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

app.Run();

public partial class Program { }
