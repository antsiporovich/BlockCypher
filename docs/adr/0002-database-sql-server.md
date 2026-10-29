# 2. Database: SQL Server 2022 in Docker, SQLite as a config-driven fallback

## Context

The task allows SQLite, a database in Docker, or NoSQL. The job posting separately names SQL Server
directly ("advanced database optimization skills for relational databases such as SQL Server"), and
ICMarkets is a .NET shop.

## Options

- **SQLite only** — explicitly permitted, zero friction, but a neutral signal: it can't demonstrate
  transient-fault handling, connection resilience, startup ordering, or server-side optimization.
- **PostgreSQL** — a real RDBMS, light, `jsonb` fits the raw-payload column well.
- **SQL Server 2022, with SQLite kept as a fallback provider.**

## Decision

**SQL Server 2022** (`mcr.microsoft.com/mssql/server:2022-latest`, pinned deliberately over the
`2025-latest` tag, which has a reported defect shipping system template databases at an older internal
version than the engine expects — not worth the marginal currency of a newer major on an unfamiliar
machine) is the primary provider, run via Docker Compose with migrations applied on startup.

**SQLite is kept as a second, config-selected provider** (`Database:Provider`), not removed. It gives a
no-Docker local path, keeps the functional test suite fast and offline, and — most importantly —
proves the Clean Architecture boundary is real: the provider is a configuration detail, not a hardcoded
assumption baked through the layers.

## Consequences

- One documented provider-specific compromise: EF Core's SQLite provider can't translate `ORDER BY`
  over `DateTimeOffset` (throws `NotSupportedException`). Fixed with a **SQLite-only value conversion**
  storing `CreatedAt` as UTC ticks (`BIGINT`); SQL Server keeps its native `datetimeoffset`. `CreatedAt`
  is always UTC, so the conversion is lossless.
- Migrations are maintained for **SQL Server only**; the SQLite path uses `EnsureCreated()`. Because
  `dotnet ef` otherwise picks its provider from runtime configuration (default `Sqlite`), an
  `IDesignTimeDbContextFactory` pins design-time tooling to SQL Server so migrations stay reproducible
  regardless of which provider is configured at the time `dotnet ef` runs.
- A composite `(Network, CreatedAt DESC)` index backs the history query; on SQL Server the nonclustered
  index implicitly carries the clustered PK, so no third index column is needed for the `Id` tiebreaker.
