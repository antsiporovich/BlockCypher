using BenchmarkDotNet.Running;

// Run with: dotnet run -c Release --project tests/ICMarkets.PerformanceTests
// (BenchmarkDotNet requires a Release build - it refuses to run under Debug).
// Pass a filter to run one class, e.g. `-- --filter *Mapping*`.
// shortcut: top-level statements already generate their own `partial class
// Program` behind the scenes, so an explicit `class Program` here collides
// with it (CS0260) - use Assembly.GetExecutingAssembly() instead of
// typeof(Program), no extra type needed.
BenchmarkSwitcher.FromAssembly(System.Reflection.Assembly.GetExecutingAssembly()).Run(args);
