using NBomber.CSharp;

// Standalone load-testing tool (not part of `dotnet test`/CI - see the
// shortcut comment in the .csproj). Point it at a running instance of the
// API, e.g.:
//
//   docker compose up -d
//   dotnet run --project tests/ICMarkets.PerformanceTests -- http://localhost:8080 btc-main
//
// Args: [baseUrl] [networkKey]. Both optional; default to a local run.
var baseUrl = (args.Length > 0 ? args[0] : "http://localhost:8080").TrimEnd('/');
var network = args.Length > 1 ? args[1] : "btc-main";

using var httpClient = new HttpClient();

var scenario = Scenario.Create("get_chain_history", async _ =>
{
    var response = await httpClient.GetAsync($"{baseUrl}/api/v1/blockchains/{network}/history?take=50");
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail(statusCode: response.StatusCode.ToString());
})
.WithoutWarmUp()
.WithLoadSimulations(
    // ramp up, hold, ramp down - throughput/latency percentiles come out of
    // the "hold" phase in the NBomber report.
    Simulation.RampingInject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
    Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
    Simulation.RampingInject(rate: 0, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(5))
);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();
