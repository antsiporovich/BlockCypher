# 4. Ingestion concurrency: bounded Channels + Task.WhenAll-style fan-out, not PLINQ

## Context

Each ingestion run fetches 5 independent BlockCypher endpoints. The task asks for "asynchronous or
parallel patterns"; the job posting's required expertise goes further, naming "deep expertise in
multi-threaded programming... thread safety, fault tolerance, efficient data stream processing"
directly.

## Options

- **Sequential `await` per network** — simplest, but 5x the wall-clock latency for no benefit, since
  the calls are independent.
- **PLINQ** (`AsParallel()`) — a natural reach for "parallelize this", but PLINQ partitions a CPU-bound
  workload across worker threads. Fetching 5 HTTP endpoints is I/O-bound: the thread is idle waiting on
  the network, not computing, so PLINQ's partitioning model is the wrong tool and wastes thread-pool
  threads parked on I/O.
- **Unbounded `Task.WhenAll(networks.Select(FetchAsync))`** — correct for I/O-bound fan-out, but with
  only 5 networks today it doesn't demonstrate backpressure, and an unbounded version would become a
  liability if the network count grew, risking tripping BlockCypher's rate limiter.
- **Bounded `System.Threading.Channels` producer/consumer + `Parallel.ForEachAsync` with an explicit
  `MaxDegreeOfParallelism`.**

## Decision

Use a **bounded `Channels` producer/consumer**, drained with **`Parallel.ForEachAsync`** under an
explicit `MaxDegreeOfParallelism`. This is I/O-bound-appropriate (async, not CPU partitioning),
demonstrates real backpressure ("efficient data stream processing" from the posting almost verbatim),
and keeps concurrency bounded rather than an unbounded fan-out that could trip BlockCypher's rate
limiting.

## Consequences

- `DbContext` is not thread-safe, so **every parallel branch resolves its own dependencies from a
  fresh DI scope** via `IServiceScopeFactory` rather than sharing one scope's `DbContext` across
  branches.
- **Per-network failure isolation**: one chain failing (timeout, 429, malformed response) is caught and
  recorded per-branch; it never aborts the batch or the other 4 networks. `POST /ingest` always returns
  `200` with a result payload — callers check `failed` for partial failures rather than getting a
  misleading `5xx` for one bad network out of five.
- `CancellationToken` is threaded end-to-end (HTTP call → channel → handler) for graceful shutdown.
- PLINQ was considered and explicitly rejected rather than silently avoided — stating the CPU-bound vs
  I/O-bound distinction is itself the signal the posting is probing for, not just "it's parallel".
