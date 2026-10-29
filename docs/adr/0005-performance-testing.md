# 5. Performance testing: BenchmarkDotNet, not NBomber

## Context

The job posting asks for "unit, integration and **performance** test coverage" — a tier almost no
take-home candidate ships. The plan named two valid tools for it: NBomber (load testing — simulates
concurrent users hitting the running API) or BenchmarkDotNet (micro-benchmarking — measures a single
method's throughput/allocations in isolation).

## Options

- **NBomber** — load-test scenarios against the running API (closer to "will this handle real traffic").
- **BenchmarkDotNet** — micro-benchmarks on specific methods (closer to "is this hot path efficient").

## Decision

Build started on **NBomber**, then switched to **BenchmarkDotNet** after checking NBomber's current
licence: NBomber's free tier is **personal-use only** — its own licensing documentation states
"you can't use it for an organization" — with no revenue threshold at all. That's stricter than the
AutoMapper/MediatR 13+/FluentAssertions 8+ situations already ruled out elsewhere in this project (see
ADR 0003), and ICMarkets is unambiguously an organization. **BenchmarkDotNet (MIT, unrestricted)**
replaces it.

## Consequences

- `tests/ICMarkets.PerformanceTests` carries two benchmark classes: `MappingBenchmarks`
  (`SnapshotMapper.ToDto`, single item and a 50-item batch) and `QueryHandlerBenchmarks`
  (`GetChainHistoryQueryHandler.Handle` against an in-memory fake repository — a real SQL Server
  round-trip belongs to the Integration suite via Testcontainers, not a micro-benchmark).
- The project is deliberately **outside** `ICMarkets.BlockCypher.slnx` and `dotnet test`/CI —
  BenchmarkDotNet needs a Release build and meaningful wall-clock time to produce stable numbers, which
  doesn't belong in a fast CI gate. Run it manually: `dotnet run -c Release --project
  tests/ICMarkets.PerformanceTests`.
- This was a mid-implementation correction, not a planning-time decision — catching and fixing this
  kind of thing is itself part of the "dependency licensing" discipline this project is built around
  (see ADR 0003).
