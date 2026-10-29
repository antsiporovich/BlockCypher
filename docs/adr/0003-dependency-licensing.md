# 3. Dependency licensing: avoid the 2025/2026 commercial traps

## Context

Several widely-used .NET libraries changed licence terms recently, moving from fully open-source to a
free tier plus a paid commercial tier. ICMarkets is well above any plausible free-tier revenue
threshold these projects use, so picking the "obvious" 2023-era default for mapping, mediation,
assertions, mocking, or load testing would mean shipping a dependency the client can't legally run in
production without a paid licence.

## Options (per concern)

| Concern | Rejected (commercial-tier risk) | Chosen |
|---|---|---|
| Object mapping | AutoMapper 15+ (RPL-1.5 + commercial; 14.x is the last free version and has an unpatched security advisory) | **Mapperly** (Apache-2.0, source-generated) |
| CQRS mediator | MediatR 13+ (RPL-1.5 + commercial) | **MediatR 12.5.0**, pinned exactly |
| Test assertions | FluentAssertions 8+ (requires a paid Xceed licence for commercial use) | **AwesomeAssertions** (Apache-2.0 fork of the last free FluentAssertions) |
| Mocking | Moq (SponsorLink privacy controversy, not a licence issue but a trust one) | **NSubstitute** (MIT) |
| Load/performance testing | NBomber (free tier is personal-use only — "you can't use it for an organization" per its own licensing docs, no revenue threshold at all, stricter than every other row here) | **BenchmarkDotNet** (MIT, unrestricted) |

## Decision

Avoid every package in the "rejected" column above; use the listed alternative instead. Enforce this
mechanically with **Central Package Management** (`Directory.Packages.props`) so every package version
is pinned in one file and a stray `dotnet add` can't silently drift a transitive or direct reference
onto a commercial major version.

## Consequences

- Mapperly's source-generation approach also happens to serve the task's explicit performance
  requirement (no runtime reflection), a free side benefit of the licensing-safe choice.
- MediatR stays pinned at exactly `12.5.0` rather than floating — a version range could resolve to a
  commercial 13.x on a future restore.
- BenchmarkDotNet was adopted **after** NBomber was initially built and its licence was checked
  mid-implementation; `tests/ICMarkets.PerformanceTests` was rewritten around
  `[MemoryDiagnoser]`/`BenchmarkSwitcher` benchmarks on the mapping and query-handler paths instead of
  NBomber load-test scenarios (see ADR 0005).
- This reasoning is repeated in the README's "Design decisions & trade-offs" section, since licensing
  terms directly affect what's safe to run in production.
