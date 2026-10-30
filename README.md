![CI](https://github.com/antsiporovich/BlockCypher/actions/workflows/ci.yml/badge.svg)

# ICMarkets BlockCypher API

A .NET 10 Web API that periodically pulls network summaries for five blockchains from the public
[BlockCypher](https://www.blockcypher.com/dev/bitcoin/) API, stores every response with a `CreatedAt`
timestamp, and exposes endpoints that return the stored **history** — newest first — rather than
proxying BlockCypher live.

📄 **[Solution Overview (PDF)](docs/ICMarkets-BlockCypher-Solution-Overview.pdf)** — a narrative walkthrough
of the design for reviewers who'd rather read a summary than trace it through the code.

## Quick start — Docker (recommended, no .NET SDK needed)

```bash
docker compose up --build
```

Then open:
- Swagger UI: http://localhost:8080/swagger
- Scalar UI: http://localhost:8080/scalar
- Health: http://localhost:8080/health
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (anonymous viewer access, admin/admin)
- RabbitMQ management UI: http://localhost:15672 (guest/guest)
- API Gateway (optional, same endpoints via a YARP reverse proxy): http://localhost:8081

This starts SQL Server 2022, RabbitMQ, the API, the optional API Gateway, Prometheus and Grafana
together. The API waits for SQL Server's and RabbitMQ's healthchecks and applies EF Core migrations on
startup, so the database schema is ready automatically.

Optional: set a free [BlockCypher token](https://accounts.blockcypher.com/) in the host shell before
`docker compose up` to raise the anonymous rate limit:

```bash
export BlockCypher__Token=your-token-here   # PowerShell: $env:BlockCypher__Token = "your-token-here"
```

## Quick start — local .NET

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore
dotnet run --project src/ICMarkets.Api
```

With no `Database:Provider` configured, the API falls back to a local SQLite file
(`src/ICMarkets.Api/blockcypher.db`, created automatically with `EnsureCreated()`) — no Docker or SQL
Server required for local development.

## Architecture

Clean Architecture layering with the dependency rule enforced one direction only, plus a
**feature-folder (vertical slice) structure inside Application** — the job posting names Clean
Architecture, Vertical Slice and CQRS, and this hybrid demonstrates all three.

```
┌─────────────────────────────────────────────────────────┐
│  ICMarkets.Api                                           │
│  controllers · OpenAPI/Swagger/Scalar · CORS · health    │
│  · DI composition root · OpenTelemetry/Serilog wiring    │
└───────────────────────────┬───────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────┐
│  ICMarkets.Infrastructure                                 │
│  EF Core DbContext · repositories · BlockCypher HTTP      │
│  client (resilience) · ingestion background worker        │
└───────────────────────────┬───────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────┐
│  ICMarkets.Application                                    │
│  CQRS commands/queries (feature folders: Ingestion,        │
│  History, Networks) · DTOs · Mapperly mappers · validators │
└───────────────────────────┬───────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────┐
│  ICMarkets.Domain                                          │
│  BlockchainSnapshot entity · Network enum · FeeRates /      │
│  GasPrices value objects · SnapshotIngested domain event    │
│  (no dependencies on any other layer)                       │
└─────────────────────────────────────────────────────────┘
```

`Api → Infrastructure → Application → Domain`. Domain depends on nothing; Application never references
Infrastructure or Api.

## Design patterns used

- **Repository** — `IBlockchainRepository` (`src/ICMarkets.Application/Abstractions/IBlockchainRepository.cs`),
  implemented by `BlockchainRepository` (`src/ICMarkets.Infrastructure/Persistence/BlockchainRepository.cs`).
  Stages changes only; it never calls `SaveChanges` itself.
- **Unit of Work** — `IUnitOfWork` (`src/ICMarkets.Application/Abstractions/IUnitOfWork.cs`), implemented
  by `AppDbContext` itself (`src/ICMarkets.Infrastructure/Persistence/AppDbContext.cs`) so a handler can
  stage several repository calls and commit them as one `SaveChangesAsync`.
- **CQRS** — MediatR 12.5.0 commands/queries under `src/ICMarkets.Application/Features/` (`Ingestion`,
  `History`, `Networks`), each with its own handler. The FluentValidation pipeline behaviour hangs off
  this same pipeline, giving automatic request validation for free.

## API endpoints

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/blockchains/ingest` | Fetch all 5 configured networks now and store a snapshot of each (per-network failure isolation — one chain failing never aborts the batch; check `failed` in the response). |
| `GET` | `/api/v1/blockchains/{network}/history?take=50&skip=0` | Stored snapshots for one network, `CreatedAt` descending. |
| `GET` | `/api/v1/blockchains/{network}/latest` | Most recently stored snapshot for one network. `404` if nothing has been ingested yet. |
| `GET` | `/api/v1/blockchains/networks` | The network keys this API accepts (`btc-main`, `eth-main`, `dash-main`, `btc-test3`, `ltc-main`). |
| `GET` | `/health` | Full health, including a DB connectivity check. |
| `GET` | `/health/live`, `/health/ready` | Kubernetes probes: liveness never touches the DB (a DB outage must not restart a healthy API); readiness includes the DB check. |
| `GET` | `/metrics` | Prometheus scrape endpoint (ingestion counters, fetch-duration histogram). |

A background worker also calls the same ingestion pipeline on a timer (`Ingestion:IntervalSeconds` in
config, default 300s) — `POST /ingest` lets you trigger it on demand, e.g. for a demo.

## Data model

`BlockchainSnapshot` (`src/ICMarkets.Domain/Entities/BlockchainSnapshot.cs`) stores one row per
ingested response:

- `Network` — the 5-value enum (`BtcMain`, `EthMain`, `DashMain`, `BtcTest3`, `LtcMain`), stored as
  `nvarchar(450)` so it can be an index key.
- The typed fields BlockCypher returns for every chain-summary call: `Name`, `Height`, `Hash`, `Time`,
  `PeerCount`, `UnconfirmedCount`, fork fields, plus `FeeRates` (BTC/LTC/Dash) or `GasPrices` (ETH) as
  value objects — exactly one of the two is populated per network family.
- `RawJson` — the complete BlockCypher response, stored as provided, so nothing captured by the typed
  columns is ever lost.
- `RawJson` is only stored for a **valid** response: an HTTP 200 whose body is empty (`{}`), an error
  object, or another network's data (wrong name, wrong fee/gas family, missing `time`) is rejected in
  `BlockCypherClient` and reported in `failed`, never persisted as a snapshot of default values.
- `CreatedAt` (`DateTimeOffset`, UTC) — the **request** time: stamped by the ingestion producer from an
  injected `TimeProvider` just *before* it calls BlockCypher and carried through the channel
  as `RequestedAt`. It is deliberately **not** the response-receipt time (that varies with latency,
  rate-limit queueing and retries) and **not** read when the row is persisted — under backpressure a
  fetched response can wait in the queue, and the history should show when the data was asked for, not
  when it was written. Not `DateTime.UtcNow`, so it stays deterministic and testable. History is always
  returned ordered `CreatedAt DESC`, with `Id DESC` as a tiebreaker for same-timestamp rows, backed by
  a composite `(Network, CreatedAt DESC)` index.

## Testing

Three mandatory projects, all green, plus a manually-run performance project:

```bash
dotnet test                                                            # everything except PerformanceTests
dotnet test --filter "FullyQualifiedName~ICMarkets.UnitTests"
dotnet test --filter "FullyQualifiedName~ICMarkets.IntegrationTests"
dotnet test --filter "FullyQualifiedName~ICMarkets.FunctionalTests"
dotnet run -c Release --project tests/ICMarkets.PerformanceTests       # BenchmarkDotNet, run manually
```

- **Unit** (`tests/ICMarkets.UnitTests`) — command/query handlers, validators, the Mapperly mapper and
  BlockCypher JSON deserialization, with NSubstitute doubles for `IChainInfoSource`/`ISender` and a hand-written fake `IUnitOfWorkFactory`.
- **Integration** (`tests/ICMarkets.IntegrationTests`) — the repository and EF Core migrations against a
  **real SQL Server** container via Testcontainers.MsSql (one shared container per test assembly). Without
  Docker the SQL Server tests are reported as **Skipped** with a reason, never as Passed; in CI
  `REQUIRE_SQLSERVER=1` turns an unavailable container into a failure instead. Testing only on SQLite would hide real SQL Server behaviour
  (identity columns, `DateTime2` precision, collation, index usage).
- **Functional** (`tests/ICMarkets.FunctionalTests`) — full HTTP pipeline via `WebApplicationFactory`,
  SQLite in-memory for speed, and **WireMock.Net** stubbing BlockCypher so the suite is fast,
  deterministic, offline, and never hits the live API's rate limits.
- **Performance** (`tests/ICMarkets.PerformanceTests`) — BenchmarkDotNet micro-benchmarks on the mapping
  and query-handler paths. Deliberately outside the `.slnx` and `dotnet test`/CI — it's a profiling
  tool, not a pass/fail gate; run it manually with `dotnet run -c Release`. These are
  microbenchmarks of in-process code (mapping, handlers over a stubbed repository); they say nothing
  about end-to-end throughput, which would be dominated by BlockCypher latency and the database.
- `TimeProvider` is injected everywhere `CreatedAt` is set, so ordering assertions are deterministic
  instead of racing the system clock.

## Configuration

All settings are under `appsettings.json` / `appsettings.Development.json`, overridable via environment
variables (`Database__Provider`, etc. — the compose file shows this).

| Setting | Values | Purpose |
|---|---|---|
| `Database:Provider` | `SqlServer` \| `Sqlite` | Picks the EF Core provider. `Sqlite` needs no Docker; `SqlServer` is what `docker-compose.yml` uses. |
| `ConnectionStrings:Default` | connection string | For the selected provider. |
| `BlockCypher:BaseUrl` | URL | BlockCypher API root. |
| `BlockCypher:FetchTimeoutSeconds` | int, default 45 | Hard budget for one network fetch (headers + body, retries included). A stalled response body counts as that network failing, never as a cancelled batch or a stopped host. |
| `BlockCypher:RequestsPerSecond` | int, default 3 | Process-wide cap on real HTTP attempts to BlockCypher (retries included), shared by every caller. Excess requests wait in a queue. |
| `BlockCypher:Token` | string, optional | Free BlockCypher token to raise the anonymous rate limit. Empty = anonymous access. |
| `Ingestion:Enabled` / `Ingestion:IntervalSeconds` | bool / int | Background ingestion worker on/off and poll interval (default 300s — deliberately modest to respect BlockCypher's rate limits). |
| `ForwardedHeaders:Enabled` / `KnownNetworks` / `KnownProxies` | bool / string[] / string[] | Honor `X-Forwarded-*` from a gateway. Off by default; when on, honored **only** from the listed CIDR networks / proxy IPs (empty lists = loopback only). `docker-compose.yml` lists the compose network. Never trust all senders: a direct caller could spoof client address, scheme and host. |
| `Cors:AllowedOrigins` | string array | Empty = allow any origin (no browser client for this assessment); set to lock down the CORS policy for a real deployment. |
| `OpenTelemetry:OtlpEndpoint` | URL, optional | Where to export OTel traces via OTLP. Empty = traces still run through the pipeline, just nothing is exported over the network. |

## Design decisions & trade-offs

- **.NET 10, not .NET 8.** .NET 8 and 9 both reach end of support on 10 November 2026, so neither is a
  good target for new work right now, even though `>= .NET 6` would be enough to build and run this.
  Risk accepted: anyone without the .NET 10 SDK installed locally uses the Docker path instead, which
  is the primary documented path and was verified by a clean-clone rehearsal.
- **SQL Server, config-driven, with a SQLite fallback.** The job posting names SQL Server directly, and
  ICMarkets is a .NET shop — so it's the primary provider in `docker-compose.yml`. The provider is a
  configuration switch (`Database:Provider`) rather than a hardcoded choice, which is what actually
  proves the Clean Architecture boundary is real rather than cosmetic: swapping providers touches no
  Application or Domain code. SQLite stays as the no-Docker local path and the functional-test database.
  One documented compromise: EF Core's SQLite provider can't translate `ORDER BY` over `DateTimeOffset`,
  so `CreatedAt` uses a provider-scoped value conversion (UTC ticks as `BIGINT`) on SQLite only; SQL
  Server keeps its native `datetimeoffset`. Migrations are maintained for SQL Server only (an
  `IDesignTimeDbContextFactory` pins `dotnet ef` to it regardless of runtime config); the SQLite path
  uses `EnsureCreated()`.
- **`Task.WhenAll`/bounded `Channels`, not PLINQ, for the 5-network fan-out.** Fetching 5 HTTP endpoints
  is I/O-bound, not CPU-bound, so PLINQ (built for CPU-bound partitioning) is the wrong tool. Ingestion
  uses a bounded `System.Threading.Channels` producer/consumer with `Parallel.ForEachAsync` and an
  explicit `MaxDegreeOfParallelism`, so concurrency is deliberately bounded rather than an unbounded
  fan-out that would trip BlockCypher's rate limiter. That cap bounds ONE batch only - it is a
  concurrency limit, not a rate limit. The rate guarantee comes from a process-wide, time-based
  minimum-interval gate (`BlockCypher:RequestsPerSecond`, default 3): consecutive requests are at least
  1/N s (~334 ms) apart, so at most N leave in ANY one-second window - a strict limit, no burst (a token
  bucket only bounds the average and can release two requests milliseconds apart). It sits in the HTTP pipeline, placed
  inside the retry strategy so every real attempt, retries included, takes a permit; all `POST /ingest`
  calls and the background worker share it, and callers over the budget queue instead of failing.
  Limits worth stating plainly: it is per process (several replicas would need a distributed limiter,
  e.g. Redis), and a per-second limit does not enforce BlockCypher's separate hourly quota - that is
  what the optional token and the 300s worker interval are for. Each persisted item gets its own unit of work
  (a fresh `DbContext`, via the `IUnitOfWorkFactory` port - Application never touches the DI container)
  because a failed `SaveChanges` poisons the context, and one network failing never aborts
  the others.
- **Dependency licensing.** Several .NET staples went commercial in 2025–2026; ICMarkets is well above
  any plausible free-tier revenue threshold, so the licence terms matter in practice, not just in
  theory:

  | Avoided | Why | Used instead |
  |---|---|---|
  | AutoMapper 15+ | RPL-1.5 + commercial; 14.x (last free) has an unpatched advisory | **Mapperly** (Apache-2.0, source-generated — also a performance win, no runtime reflection) |
  | MediatR 13+ | RPL-1.5 + commercial | **MediatR 12.5.0**, pinned exactly via Central Package Management |
  | FluentAssertions 8+ | Requires a paid Xceed licence for commercial use | **AwesomeAssertions** (Apache-2.0 fork) |
  | Moq | SponsorLink privacy controversy | **NSubstitute** (MIT) |
  | NBomber | Free tier is personal-use only — its own docs say "you can't use it for an organization" (stricter than the others; no revenue threshold at all) | **BenchmarkDotNet** (MIT, no paid tier) |

  Enforced mechanically: every version lives in `Directory.Packages.props` (Central Package Management),
  so a stray `dotnet add` can't silently drift onto a commercial version.
- **The API serves stored history, not a live BlockCypher proxy.** This is the task's own requirement —
  `CreatedAt` is set once, at request time, and every read endpoint queries the database, never BlockCypher
  directly. This is also what makes the functional tests fast, deterministic and independent of
  BlockCypher's availability or rate limits (WireMock stands in for BlockCypher there).
- **Messaging: at-least-once, not exactly-once.** `SnapshotIngested` is written to an outbox table in the
  same transaction as the snapshot, then drained to RabbitMQ by `OutboxPublisherWorker`. The delivery
  contract, and what enforces it:
  - A row is marked processed only after the broker has **confirmed** the message — the channel uses
    publisher confirms with tracking, so `BasicPublishAsync` completes on the broker's ack, not when bytes
    reach the socket — and a nack throws.
  - Publishes are `mandatory` against a fanout exchange: a message no queue can receive is returned by the
    broker and surfaces as an exception, so the row stays unprocessed instead of being "delivered" into
    nothing. For the demo a durable, TTL/length-bounded queue is declared and bound
    (`Outbox:Queue`, empty = declare nothing and let real consumers bind their own).
  - Messages are `Persistent` (with a durable queue they survive a broker restart) and carry
    `MessageId` = the outbox row id, stable across retries.
  - Each publish has its own time budget (`Outbox:PublishTimeoutSeconds`, default 10): RabbitMQ.Client
    waits for a confirm with no timeout, so without it a broker that stays connected but stops
    confirming would stall the queue indefinitely. When the budget runs out the attempt is abandoned,
    the channel is recreated (its confirm state is undefined after a cancelled publish) and the next
    tick retries; a late confirm for the abandoned attempt is one more reason consumers de-duplicate.
  - Broker down, nack or unroutable → the row stays unprocessed and is retried on the next tick. Each row
    is saved as soon as it is confirmed, so a failure later in a batch does not re-publish confirmed rows.
  - The remaining duplicate window — a crash between the broker's ack and saving `ProcessedAt` — is
    closed on the consumer side: **consumers must be idempotent on `MessageId`.** No exactly-once claim.
  - Staging a row is idempotent within a unit of work: a failed-then-retried `SaveChanges` does not
    produce two outbox rows for one snapshot.

## Requirement coverage

The requirements below map each capability — domain-driven microservices, multithreading and
concurrency, Clean/Vertical Slice/CQRS, event-driven messaging, full observability, CI/CD,
Kubernetes — to where it lives in this codebase, rather than leaving it implicit:

| Requirement | Where it lives here |
|---|---|
| Multithreading, concurrency, thread safety, fault tolerance | Bounded `System.Threading.Channels` + `Parallel.ForEachAsync` ingestion pipeline, a fresh unit of work per persisted item (`IUnitOfWorkFactory` port), per-network failure isolation — see "Design decisions & trade-offs" above and ADR 0004. |
| Clean Architecture + Vertical Slice + CQRS | Four-layer Clean Architecture with feature folders (`Application/Features/{Ingestion,History,Networks}`) inside it; CQRS via MediatR. See "Architecture" and "Design patterns used" above. |
| Domain-driven | `BlockchainSnapshot` as an entity whose static factory enforces its own invariants (non-empty name/hash/raw payload, non-negative height and counts), `FeeRates`/`GasPrices` value objects, a `SnapshotIngested` domain event — deliberately scoped short of full tactical DDD, which five snapshot tables don't justify. |
| Event-driven design (Kafka/AWS SQS/RabbitMQ) | Transactional outbox + **RabbitMQ** publisher on `SnapshotIngested` (`Infrastructure/Messaging/`), off by default, on in `docker-compose.yml`. A fanout exchange means a Kafka or SQS consumer could subscribe without this publisher changing; swapping the exchange for a Kafka topic is a new `IEventPublisher` implementation, not a redesign. |
| Saga pattern for distributed transactions | Not implemented — this service has one aggregate and no cross-service transaction to coordinate. Noted here as the natural next step if ingestion and persistence ever split into separate services. |
| OpenTelemetry, Prometheus, Grafana, Elastic/Kibana | Full OTel traces/metrics/logs, `/metrics` scraped by Prometheus, a provisioned Grafana dashboard in `docker-compose.yml`, and Elastic Common Schema-formatted console logs (Kibana-ingestible without standing up a full ELK stack). |
| TDD, unit/integration/performance tests | Four test projects: Unit, Integration (real SQL Server via Testcontainers), Functional (WireMock-stubbed), Performance (BenchmarkDotNet). See "Testing" above. |
| CI/CD | `.github/workflows/ci.yml` — restore, build, test on every push. |
| Docker, Kubernetes | Multi-stage Dockerfiles for every service; `docker-compose.yml` is the primary, verified run path; `k8s/` has Deployment/Service/ConfigMap manifests for the same stack (best-effort, not cluster-tested — see `k8s/README.md`). |
| Advanced SQL Server optimization | Config-driven provider, composite `(Network, CreatedAt DESC)` index, `EnableRetryOnFailure`, pinned image version — see ADR 0002. |
| Agile/Scrum, code review habits | Scoped, meaningful commit history across `main`/`development` rather than one large commit; see the repository's own history. |

**If this grew past one service**: the ingestion worker and the read API are already only coupled
through the database and the outbox — splitting them into an ingestion service and a read service
communicating over the same RabbitMQ exchange (or Kafka/SQS in a larger deployment) would be a
redeployment, not a rewrite. The optional **YARP API Gateway** (`src/ICMarkets.Gateway`) is a first
step in that direction: a thin reverse proxy that would sit in front of however many backend services
this became, while every route currently lives on one.

## Known limitations / what I'd do next

- **No auth** — out of scope for the assessment brief; a real deployment would put this behind
  JWT/OAuth2 and scope the CORS policy to real origins.
- **No distributed cache** — `GetLatestSnapshotQuery` hits the database on every call; a short-TTL
  Redis cache in front of it would cut load under real traffic without affecting ingestion correctness.
- **RabbitMQ only, not Kafka/SQS** — `IEventPublisher` is the seam for either; RabbitMQ was the
  cheapest real broker to run in `docker-compose.yml` for this exercise.
- **Kubernetes manifests aren't cluster-tested** — `docker-compose.yml` remains the verified path; see
  `k8s/README.md` for what's simplified.
- **Swagger/Scalar/OpenAPI are exposed unconditionally, including in `Production`** — the k8s manifests
  set `ASPNETCORE_ENVIRONMENT=Production` and the docs are still reachable there (the k8s README routes
  reviewers through `/swagger` via port-forward). Intentional for this assessment, so a reviewer doesn't
  need a second environment switch just to see the API docs; a real deployment would gate
  `MapOpenApi`/`MapScalarApiReference`/`UseSwaggerUI` behind `app.Environment.IsDevelopment()` or a
  feature flag.
- **Read path loads `RawJson`** — `GetHistory`/`GetLatest` materialize the full entity, including the
  large `RawJson` audit column, and the mapper then drops it. A server-side projection straight to the DTO
  would avoid that; negligible at this row size, so it is noted rather than done.
- **Outbox retention** — `OutboxPublisherWorker` purges processed rows older than
  `Outbox:RetentionHours` (default 24h) on every tick, and the table is never written to at all while
  `Outbox:Enabled` is false (the default local/SQLite path), so it stays bounded in both modes. The purge
  runs on SQLite too (`ProcessedAt` is stored as UTC ticks there so the comparison translates).

See `docs/adr/` for the fuller reasoning behind the choices above.
