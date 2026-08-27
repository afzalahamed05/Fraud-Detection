# Interview Notes

## Architecture

```
Angular 20 dashboard  <-->  ASP.NET Core Web API (JWT auth, EF Core, Serilog)
                                   |
                        POST /api/transactions
                                   |
                    Postgres insert (Status=Pending)   <- durable before Kafka is touched
                                   |
                         Kafka producer (inline retry)
                                   v
                    Kafka topic: transactions.created (3 partitions, keyed by AccountId)
                                   |
                  Scala Spark Structured Streaming (5s micro-batch trigger)
                        - 6 deterministic rules, configurable weights/thresholds
                        - reads customer_risk_profiles for 2 of those rules
                        - writes Status/RiskScore + FraudAlert via raw JDBC
                                   |
                    Postgres (transactions, fraud_alerts, customer_risk_profiles)
                                   ^
                  PySpark batch job (every 30s)
                        - recomputes customer_risk_profiles from ALL history
                        - z-score anomaly detection -> FraudAlert (Source=PySparkAnomalyDetection)
                                   |
                        .NET API reads it all back out
                                   |
                        Angular polls every 3s, renders it
```

Postgres is the single source of truth every component reads from and writes to — Kafka is
the transport for the real-time hop, not a system of record.

## Why each technology

- **.NET / ASP.NET Core**: strongly-typed API surface, EF Core migrations give a real
  schema history, built-in DI made wiring health checks/auth/logging straightforward.
  No deeper reason than "solid, well-understood choice for a REST API" — not the
  interesting part of this project.
- **Angular**: the dashboard needed real routing (list/detail pages), reactive state
  (signals + RxJS for 3-second polling), and strong typing shared conceptually with the
  C# DTOs. A SPA framework earns its keep here because the UI has real navigable state,
  not just one page.
- **Kafka**: decouples "accept the transaction" from "score the transaction." The API can
  return instantly (transaction is durably in Postgres) while scoring happens asynchronously,
  and a scoring outage doesn't back up the write path — it just delays scoring, which the
  outbox sweep and Kafka's own durability protect against losing.
- **Scala + Spark Structured Streaming**: needed a real-time, stateful stream processor with
  first-class Kafka integration. Scala specifically because Structured Streaming's Scala API
  is the primary/most complete one, and the JVM gives predictable low-latency per-row
  processing (3–25ms measured) for the rule evaluation itself.
- **PySpark**: batch analytics over the *entire* transaction history (customer baselines, z-
  score anomaly detection) is a different workload shape than streaming — full-table scans,
  aggregations, no per-event latency requirement. Python because pandas-adjacent data
  manipulation and PySpark's DataFrame API are a more natural fit for that kind of batch
  statistics job than writing the same thing in Scala would have been, and it's a deliberate
  demonstration of using the right tool per workload rather than a single "big data" hammer.

## How the streaming pipeline actually works

1. `POST /api/transactions` inserts a `Pending` row, *then* tries to publish to Kafka. If
   Kafka is down, the row still exists — a background `TransactionOutboxService` retries
   anything with `PublishedToKafkaUtc == null` every 15s.
2. Scala's Structured Streaming query reads `transactions.created` on a 5-second
   `ProcessingTime` trigger. Each micro-batch is processed via `foreachBatch` +
   `foreachPartition`, opening one JDBC connection per partition (not per row).
3. For each transaction: query recent-transaction count (velocity), today's count
   (frequency), and the customer's profile (escalation/frequency rules use it) — all via
   raw JDBC with parameterized queries, not an ORM.
4. `RiskRules.evaluate()` is a pure function (case classes in, `RiskAssessment` out) — no
   Spark dependency, which is why it's unit-testable with zero SparkSession overhead.
5. The result is written back with an idempotency guard (`WHERE "Status" = 'Pending'`), so
   redelivery after a crash before the offset commit is a safe no-op.
6. Independently, PySpark re-reads *all* transactions every 30s, recomputes
   `customer_risk_profiles`, and flags z-score outliers Scala's fixed thresholds wouldn't
   catch — feeding back into Scala's next cycle.

## Biggest engineering challenges (things that actually broke)

- **Silently swallowed logs**: `SparkContext.setLogLevel("WARN")` resets the root logger in
  a way that ate the app's own `logger.info(...)` calls even with a bundled
  `log4j2.properties`. Diagnosed via a JVM thread dump (`kill -QUIT`) to prove the query
  wasn't actually stuck, then fixed with an explicit `Configurator.setLevel(...)` call.
- **Spark's JDBC writer can't preserve `uuid` columns**: `DataFrame.write.jdbc()` round-trips
  Postgres `uuid` columns as plain strings, so writing back fails with a type mismatch.
  Fixed with explicit `::uuid` casts (raw JDBC in Scala, `psycopg2` in PySpark).
- **A startup race condition found during the final audit**: PySpark's first run after a
  cold start failed because it only waited on Postgres, not on the API having finished its
  EF Core migration. Fixed with a proper Docker `HEALTHCHECK` on the API and
  `depends_on: condition: service_healthy`.
- **Kafka has no persistent volume by default**: a `docker compose down`/`up` cycle silently
  wiped the topic while Postgres (named volume) survived — looked exactly like a broken
  consumer until traced to the missing volume.

## Tradeoffs made deliberately

- **Velocity/frequency rules query Postgres per micro-batch** instead of maintaining
  in-memory Spark state (`flatMapGroupsWithState`). Simpler, correct, and fast enough (3–25ms
  measured) — the DB round trip is cheap at this data volume. A `flatMapGroupsWithState`
  version would avoid the DB hit but adds real complexity (state store management, watermark
  tuning) that wasn't justified yet.
- **5-second micro-batch trigger** is the dominant factor in end-to-end latency (avg ~2.4s,
  p95 ~6s measured), even though actual compute is 3–25ms. Shortening it would cut latency
  at the cost of smaller, less efficient batches — not tuned further because nothing in the
  requirements demanded sub-second latency.
- **PySpark does a full-table read every 30s** rather than an incremental read. Correct and
  simple at hundreds of rows; would need windowing/incremental aggregation at real scale.
- **Single seeded demo admin, no user table.** Deliberately minimal — the point was to
  demonstrate JWT auth is wired correctly (PBKDF2 hashing, protected mutating endpoints,
  fail-fast on missing signing secret), not to build a user management system.

## Scalability discussion

- **Kafka partitions (3) cap Scala's parallelism** at 3 concurrent consumers in the group;
  more partitions would let more executor cores work in parallel. Straightforward to
  increase — no code change, just topic config.
- **PySpark's full-table scan** is the first thing that breaks at real scale — it would need
  to become incremental (only recompute profiles for accounts with new transactions since
  the last run) well before the transaction table reaches millions of rows.
- **Single Postgres instance is the actual bottleneck** in this architecture — every
  component (API, Scala, PySpark) reads/writes it directly. Read replicas for the
  API's GET endpoints would be the first scaling move; the write path (API inserts, Scala
  updates) is harder to shard without changing the consistency model.
- **The 5-second trigger interval is a dial, not a wall** — it directly trades latency for
  batch-size efficiency and is the first thing to tune if a real SLA required faster
  scoring.

## 10 likely interview questions

**1. Why not just do fraud scoring synchronously in the API, like you did in Phase 1?**
Because scoring needs to query recent history (velocity) and customer profiles, which don't
scale well as *every* API request's critical path — decoupling it means the write path stays
fast and predictable regardless of how expensive scoring logic gets, and a scoring outage
delays rather than blocks transaction creation.

**2. Why two Spark engines and not one?**
They solve different problems on different time horizons: Scala's per-event rules ("is this
objectively risky") need low latency and see one transaction at a time; PySpark's anomaly
detection ("is this unusual for this customer") needs full history and doesn't have a
latency requirement. Running both as one job would force one latency profile on both.

**3. How do you guarantee a transaction isn't lost if Kafka goes down?**
It's written to Postgres as `Pending` *before* the Kafka publish is attempted. If publish
fails, `PublishedToKafkaUtc` stays null, and a background sweep retries it every 15s. Kafka
being down delays scoring; it never loses the transaction.

**4. What happens if the Scala consumer crashes mid-message?**
Offsets are committed manually, only after the Postgres write succeeds. A crash before that
commit means the message is redelivered on restart. The `WHERE "Status" = 'Pending'` guard
on the update makes reprocessing an already-scored transaction a safe no-op.

**5. How did you actually measure latency, not just estimate it?**
Every transaction has `PublishedToKafkaUtc` and `ProcessedAtUtc` timestamps in Postgres.
I queried `AVG`/`PERCENTILE_CONT(0.95)` of their difference directly, both for an isolated
transaction and for a 40-transaction burst, and separately read Scala's own per-row
`latencyMs` log to isolate compute time from queueing time.

**6. Why is p95 latency (~6s) so much higher than the per-row compute time (~20ms)?**
Structured Streaming's 5-second `ProcessingTime` trigger means a transaction arriving right
after a batch starts waits almost a full trigger interval before the *next* batch picks it
up. That queueing wait, not compute, dominates end-to-end latency.

**7. How would you scale this to 10x the transaction volume?**
Increase Kafka partitions (more parallel Scala consumers), make PySpark's profile
recomputation incremental instead of a full-table scan, and likely add a Postgres read
replica for the API's GET-heavy dashboard traffic before touching the write path.

**8. What would you do differently with more time?**
Route poison Kafka messages to a real dead-letter topic instead of just logging and
committing past them; move `flatMapGroupsWithState` in for velocity so it doesn't need a DB
round trip; replace the single demo admin with real user management.

**9. How is the JWT secret managed, and why does it matter?**
It's not committed — `docker-compose.yml` reads it from an `AUTH_JWT_SECRET` environment
variable (`.env`, gitignored), and fails fast (`${VAR:?message}`) if it's missing. The API
itself also refuses to start with an empty signing key, so a misconfiguration can't
silently produce forgeable tokens.

**10. How did you verify the whole pipeline actually works, versus just each piece in
isolation?**
An end-to-end smoke test script (`scripts/e2e-smoke-test.sh`) logs in, creates a normal and
a high-risk transaction through the real API, polls until Scala scores both, and asserts on
the resulting status, risk score, and fraud alert — run against the live Docker Compose
stack, not mocks. It's part of this repo and passes 10/10 as of this audit.
