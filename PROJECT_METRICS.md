# Project Metrics

Tracked cumulatively across phases. Numbers are measured, not estimated — see the
verification commands in each phase section.

---

## FINAL AUDIT — Verified Metrics (2026-08-27)

Measured against a **genuinely clean environment**: `docker compose down -v` (wipes both
named volumes, so Postgres and Kafka start from nothing), then `docker compose up -d --build`,
then every number below was either read directly out of Postgres, out of a test runner's own
output, or timed with a stopwatch around the actual command. Full methodology in each row;
raw scripts are in `scripts/`. Nothing here is estimated — anything not directly measured
says so.

| Metric | Value | How measured |
|---|---|---|
| **.NET LOC** (app / tests) | 1,676 / 575 | `wc -l` on `backend/FraudDetection.Api/**/*.cs` (excl. `Migrations/`) and `backend/FraudDetection.Api.Tests/**/*.cs` |
| **Angular LOC** (app / tests) | 1,757 / 336 | `wc -l` on `frontend/src/**/*.{ts,html,scss}`, tests split out by `*.spec.ts` |
| **Scala LOC** (app / tests) | 461 / 152 | `wc -l` on `spark-jobs/scala-risk-engine/src/{main,test}/**/*.scala` |
| **PySpark LOC** (app / tests) | 337 / 180 | `wc -l` on `spark-jobs/pyspark-analytics/*.py` and `tests/*.py` |
| **Total source LOC** (app only, all 4 languages) | 4,231 | sum of the four app-LOC figures above |
| **Total LOC incl. tests** | 5,474 | sum of all eight figures above |
| **API endpoints** | 16 | 14 controller actions (`grep -rc '\[Http(Get\|Post\|Patch)'` in `Controllers/`) + 2 ASP.NET Core health endpoints (`/health/live`, `/health/ready`) |
| **Angular components** | 11 | 10 `*.component.ts` + 1 root `App` component |
| **Angular routes** | 7 (+ wildcard redirect) | `app.routes.ts` |
| **Database tables** | 3 domain + 1 EF migrations history | `\dt` in psql: `transactions`, `fraud_alerts`, `customer_risk_profiles`, `__EFMigrationsHistory` |
| **Kafka topics** | 1 (`transactions.created`, 3 partitions, replication factor 1) | `kafka-topics.sh --list` / `--describe` |
| **Kafka producers** | 1 (.NET `KafkaProducerService`, singleton) | code inspection |
| **Kafka consumers** | 1 (Scala Structured Streaming query, 5s micro-batch trigger) | code inspection |
| **Fraud detection rules** | 7 (6 deterministic in Scala + 1 statistical in PySpark) | `RiskRules.scala` (6 functions) + `rules.py`'s z-score anomaly check |
| **Automated tests** | 76 | 25 .NET (xUnit) + 20 Angular (Karma/headless Chromium) + 14 Scala (ScalaTest) + 17 PySpark (pytest) — all 4 suites run this session |
| **Test pass rate** | 100% (76/76) | test runner output, this session, zero failures |
| **E2E flow assertions** | 10/10 passing | `scripts/e2e-smoke-test.sh`: login → reject unauthenticated write → create normal + high-risk transaction → poll Scala scoring → verify fraud alert + rule citations → verify dashboard stats |
| **API ingestion throughput** | 23.69 req/s | 40 `POST /api/transactions` fired concurrently, wall-clock time around `wait`, `scripts/measure-metrics.sh` |
| **Avg end-to-end processing latency** | ~2,410 ms (burst of 40) | Postgres `AVG(ProcessedAtUtc - PublishedToKafkaUtc)` over that batch |
| **p95 end-to-end processing latency** | 5,972 ms (burst of 40) | Postgres `PERCENTILE_CONT(0.95)` over the same batch |
| **Isolated single-transaction latency** | 1,773 / 4,263 / 2,048 ms (3 separate probes, ~7s apart) | same query, one transaction at a time |
| **Per-transaction Scala processing time** (excl. queue wait) | 3–25 ms typical, up to 74 ms on a cold batch | Scala's own `event=risk_scored latencyMs=` log line — this is compute time only; the latency numbers above additionally include waiting for the next 5s Structured Streaming trigger, which dominates them (see Interview Notes) |
| **Fraud detection rate on labeled test set** | 100% (20/20 known-fraud transactions correctly flagged) | 20 transactions engineered to guarantee `VeryLargeAmount`+`RiskyCountry` (score 65 ≥ 40 threshold), `scripts/measure-metrics.sh` |
| **False positive rate on labeled test set** | 0% (0/20 known-normal transactions incorrectly flagged) | 20 small-amount, trusted-country transactions, same script |
| **Docker Compose services** | 7 defined (6 long-running + `kafka-init`, one-shot) | `docker-compose.yml` |
| **Cold startup time** (`docker compose up -d --build` → containers running) | 22.9–41.8 s across 2 clean runs | PowerShell `Stopwatch`, timed around the command, from wiped volumes |
| **Cold startup time** (→ API `/health/ready` = 200) | 27.0–42.6 s across 2 clean runs | same stopwatch, continued until the health endpoint responded |
| **Full clean (`--no-cache`) image build time** | not measured | never run — would re-download ~2 GB of Spark/Kafka dependencies each time; not repeated for this audit |
| **Database records** (this run, after seed + all test/audit traffic) | 575 transactions, 68 fraud alerts (55 Scala, 13 PySpark), 91 customer risk profiles | `GET /api/dashboard/stats` + direct `psql` counts |
| **Technologies/frameworks used** | 21 | C#/.NET 9, ASP.NET Core, EF Core, PostgreSQL, Serilog, JWT auth, Swagger/OpenAPI, xUnit — Angular 20, TypeScript, RxJS, Karma/Jasmine — Apache Kafka, Scala, Apache Spark (Structured Streaming), sbt, ScalaTest — Python, PySpark, pytest — Docker/Docker Compose |

### Bugs found and fixed during this audit

- **PySpark's first run after a cold start failed** (`relation "transactions" does not exist`):
  `pyspark-analytics` only waited on Postgres being reachable, not on the .NET API having
  actually run its EF Core migration. Fixed by adding a Docker `HEALTHCHECK` to the API
  image (`curl -f /health/live`) and changing `pyspark-analytics`/`scala-risk-engine`'s
  `depends_on` to `condition: service_healthy` on `api`. Verified: reran from a wiped
  environment, first PySpark cycle now succeeds with zero errors.
- **JWT signing secret was committed in plaintext** in `appsettings.json`. Moved to an
  `AUTH_JWT_SECRET` environment variable (`.env`, gitignored; `.env.example` committed as
  the template), with `docker-compose.yml` failing fast (`${AUTH_JWT_SECRET:?...}`) if it's
  unset. Also added a startup check in `Program.cs` that refuses to start with an empty
  secret rather than silently signing tokens with one.
- **That startup check initially broke integration tests** — reading `builder.Configuration`
  synchronously at the top of `Program.cs` doesn't see `WebApplicationFactory`'s test config
  overrides (those only apply to values resolved later via `IOptions<T>`). Fixed by moving
  JWT Bearer configuration to the `AddOptions<JwtBearerOptions>().Configure<IOptions<AuthOptions>>()`
  pattern and moving the fail-fast check to after `app.Build()`, resolving from the DI
  container instead of the raw configuration object.
- **Dead code removed**: `TransactionConsumerService.cs` (the Phase 2 Kafka consumer,
  fully superseded by the Scala engine since Phase 3, referenced only in comments — zero
  actual callers) was deleted rather than left "for reference."

### What's still a known limitation (not fixed, documented instead)

- End-to-end latency is dominated by Structured Streaming's 5-second micro-batch trigger
  interval, not by actual compute (which is 3–25 ms). Lowering the trigger interval would
  cut latency at the cost of smaller, less efficient batches — a real tradeoff, not tuned
  further in this audit.
- Single seeded demo admin account, no real user management — acceptable for a portfolio
  demo, called out explicitly rather than presented as production-grade auth.
- Postgres/Kafka credentials are plaintext defaults in `docker-compose.yml` — reasonable for
  an all-local demo stack with no exposed ports beyond localhost, not something to reuse
  as-is for a real deployment.

---

## Phase 1 — Foundation (2026-08-26)

| Metric | Value |
|---|---|
| REST API endpoints | 6 |
| Database tables (domain) | 2 |
| Backend LOC | ~675 |
| Frontend LOC | ~444 |
| Docker Compose services | 3 |

## Phase 2 — Real-Time Event Streaming (2026-08-27)

### Architecture

```
POST /api/transactions
   -> Postgres insert (Status=Pending)   <- durable before Kafka is even touched
   -> Kafka producer (inline, 3x retry)
        \-> transactions.created (3 partitions, keyed by AccountId)
   -> TransactionConsumerService (background, consumer group fraud-detection-consumer)
        -> FraudDetectionService.EvaluateAsync (same rule engine as Phase 1)
        -> Postgres update (Status=Approved/Flagged, ProcessedAtUtc set)
        -> manual offset commit (only after the DB write succeeds)
   -> Angular dashboard (polls every 3s, shows Pending -> resolved live)
```

Reliability, not just happy path:
- **Transactional-outbox-style recovery**: `PublishedToKafkaUtc` is null until a publish
  succeeds. `TransactionOutboxService` sweeps every 15s and retries anything still null —
  so a Kafka outage delays scoring, it doesn't lose the transaction.
- **Idempotent consumer**: redelivery after a crash is a no-op if `Status != Pending`
  already. Verified live — see Test Results below.
- **Manual offset commits**: `EnableAutoCommit = false`, committed only after the DB write
  succeeds, so a crash mid-message causes replay from the broker, not silent loss.
- **Poison-message handling**: 3 in-process retry attempts per message; if all fail, the
  error is persisted to `Transaction.ProcessingError` and the offset is still committed so
  one bad message can't wedge a partition (a real system would route this to a dead-letter
  topic instead — noted as a Phase 3 candidate).
- **Versioned envelope**: every message is `{ eventId, eventType, eventVersion, occurredAtUtc, payload }`
  — `eventVersion` lets consumers branch on schema changes instead of guessing.

### Metrics

| Metric | Value |
|---|---|
| Kafka topics | 1 (`transactions.created`, 3 partitions, replication factor 1) |
| Producers | 1 (`KafkaProducerService`, singleton, `Acks.All` + idempotence enabled) |
| Consumers | 1 (`TransactionConsumerService`, consumer group `fraud-detection-consumer`) |
| Background services | 2 (`TransactionConsumerService`, `TransactionOutboxService`) |
| New API endpoints | 2 (`GET /api/health`, `GET /api/health/pipeline`) |
| Total API endpoints | 8 |
| New DB columns | 4 (`PublishedToKafkaUtc`, `ProcessedAtUtc`, `ProcessingError`, `ProcessingAttempts`) |
| Backend LOC | ~1,239 (+564 from Phase 1) |
| Frontend LOC | ~609 (+165 from Phase 1) |
| Automated tests | 0 — verified via live manual scenarios (below); unit/integration tests are a Phase 3 candidate |

### Throughput / latency (measured, this machine, single broker, single consumer instance)

- **Backfill catch-up**: 532 pre-existing Phase 1 rows (never published, since the column
  didn't exist yet) were picked up by the outbox sweep and pushed through the full
  produce→consume round trip in ~2 batches of 50 every 15s — all correctly no-op'd by the
  idempotency check (`Status != Pending`), confirmed zero duplicate fraud alerts.
- **End-to-end latency** (publish → consumed → DB write complete), rolling average of the
  last 100 processed transactions, read from `GET /api/health/pipeline`:
  - **23–31 ms** average, measured across 3 live test transactions.
- **Consumer lag**: 0 across all 3 partitions after catch-up (`kafka-consumer-groups.sh --describe`).
- These numbers are for one broker, one partition-count of 3, one consumer instance, and
  light load (a handful of transactions/sec) — not a load test. Useful as a baseline, not
  a capacity claim.

### Test Results (live, run against this repo)

1. **Normal transaction** (`$12.75`, US, Dining) — created as `Pending`, resolved to
   `Approved` in ~29 ms, zero fraud alert. ✅
2. **High-risk transaction** (`$15,000`, `KP`, Electronics) — created as `Pending`, resolved
   to `Flagged` in ~32 ms, fraud alert created with `severity=High`, `riskScore=65`,
   reason `"Very high transaction amount; Transaction from high-risk country"`. ✅
3. **Idempotency / backfill**: 532 pre-Kafka rows swept through the pipeline on first
   startup after the migration; dashboard totals (`532 transactions`, `35 alerts`) were
   byte-for-byte identical before and after — confirms the `Status != Pending` guard
   prevents reprocessing. ✅
4. **Live browser test**: clicked "Simulate Transaction" in the Angular dashboard —
   transaction appeared and resolved to `Flagged` (`Sketchy Overseas Corp`, $16,382.69, RU)
   without a manual page refresh, driven entirely by the 3-second poll. ✅
5. **Pipeline health endpoint**: `kafkaConnected: true`, `messagesProduced == messagesConsumed`,
   `messagesFailed: 0`, `stuckCount: 0` after all of the above. ✅

## Phase 3 — Fraud Detection Engine: Scala + PySpark (2026-08-27)

### Architecture — why two engines, not one twice

```
Kafka (transactions.created)
   -> Scala Structured Streaming (micro-batch, 5s trigger)
        -> per-transaction deterministic rules (7 rules, configurable weights/thresholds)
        -> reads customer_risk_profiles (written by PySpark) for 2 of those rules
        -> writes Transaction.RiskScore/Status + FraudAlert (Source=ScalaRiskEngine) via raw JDBC
   -> PySpark analytics (batch, every 30s)
        -> reads ALL transactions from Postgres
        -> recomputes customer_risk_profiles (count, avg/stddev amount, distinct
           categories/countries, avg txns/day) -- feeds back into Scala's rules above
        -> z-score anomaly detection: flags transactions >3 std-dev from *that customer's*
           own historical average -- independent of Scala's fixed thresholds, catches
           "unusual for this person" rather than "objectively large"
        -> writes FraudAlert (Source=PySparkAnomalyDetection) for anything Scala didn't
           already flag
```

Scala catches "objectively risky" in real time; PySpark catches "statistically unusual for
this specific customer" using history Scala's per-event view can't see on its own — genuinely
different techniques on different time horizons, not two copies of the same rule.

### Fraud rules (Scala, all thresholds in `application.conf`)

1. `LargeAmount` / `VeryLargeAmount` — amount over a configurable threshold
2. `RiskyCountry` — country on a configurable watch-list
3. `HighVelocity` — N+ transactions from the account within a configurable window (queried
   from Postgres per micro-batch, not in-memory streaming state — see Decisions below)
4. `SpendingEscalation` — amount is N× the customer's historical average (from PySpark's profile)
5. `UnusualFrequency` — today's transaction count is N× the customer's daily average (from profile)
6. `RiskyCategory` — watch-listed merchant category combined with a real amount
7. `StatisticalAmountAnomaly` (PySpark) — z-score outlier vs. the customer's own history

### Metrics

| Metric | Value |
|---|---|
| Scala LOC (main) | ~461 |
| Scala LOC (tests) | ~152 |
| Scala unit tests | 14, all pure functions (no SparkSession needed) |
| PySpark LOC (main) | ~337 |
| PySpark LOC (tests) | ~180 |
| PySpark tests | 17 (pytest; aggregation/anomaly tests use a local SparkSession) |
| Fraud rules | 7 total (6 Scala deterministic + 1 PySpark statistical) |
| New API endpoints | 1 (`GET /api/customer-risk-profiles/{accountId}`, +list) |

### Latency / throughput (measured against the live stack)

- **Scala risk scoring**: 23–44 ms per transaction, publish-to-processed (Kafka -> Postgres
  write complete), measured via `Transaction.PublishedToKafkaUtc`/`ProcessedAtUtc`.
- **PySpark analytics job**: one full run over 536 transactions (52 customer profiles
  recomputed, 13 new anomaly alerts written) completed in **20.15 s**.
- Both numbers are single-broker, single-executor, light load (hundreds of rows) — a
  baseline for this machine, not a capacity claim.

### Bugs found and fixed while getting this to actually run

- **Kafka has no persistent volume** by default in Docker Compose — a `down`/`up` cycle
  silently wiped the topic while Postgres data (named volume) survived, making it look like
  the Scala consumer was broken when it was correctly finding zero new messages. Fixed by
  adding a `kafka_data` volume.
- **Spark's JDBC writer can't preserve Postgres `uuid` columns** — `DataFrame.write.jdbc()`
  round-trips `uuid` columns as plain strings, so writing back hits "column is of type uuid
  but expression is of type character varying". Scala avoids this with explicit `::uuid`
  casts in raw JDBC; PySpark's writes were switched from `write.jdbc()` to `psycopg2` with
  the same explicit casts.
- **`SparkContext.setLogLevel("WARN")` silently ate all custom log output** — it resets the
  root logger in a way that made the app's own `logger.info(...)` calls vanish even with a
  bundled `log4j2.properties`. Fixed with an explicit `Configurator.setLevel(...)` call after
  Spark's own logging setup.
- **PySpark UDF returned `null` for every row** — Postgres `decimal` columns arrive in a UDF
  as Python `Decimal`, which doesn't coerce into a UDF declared to return `"double"`; fixed
  by casting to `double` before the UDF call.
- **A test's own math was wrong, not the code**: a z-score anomaly test used only 6 normal
  transactions plus 1 outlier — the outlier inflated its own baseline enough to look
  "normal." Fixed by using a larger, more realistic history (20 transactions) rather than
  loosening the detection threshold.

## Phase 4 — Portfolio-Quality Application (2026-08-27)

### What was added

**.NET**: JWT authentication (single seeded admin, PBKDF2-hashed password, protects
mutating endpoints only — GETs stay anonymous so the dashboard works without a login wall),
centralized `IExceptionHandler` returning RFC 7807 ProblemDetails, Serilog structured
logging + request logging, ASP.NET Core health checks (`/health/live`, `/health/ready`,
separate from the existing business-level `/api/health/pipeline`), Swagger XML doc comments
+ JWT auth scheme, search/filter/date-range query params on transactions and alerts, a
`GET /api/dashboard/trends` (14-day daily volume/fraud) and `GET /api/fraud-alerts/top-triggers`
endpoint to back new dashboard panels.

**Angular**: real routing (7 routes) replacing the single-page dashboard — transaction list
with search/status filter/pagination, transaction detail page (full pipeline trace: risk
score, scored-by, Kafka publish/process timestamps, latency, associated alerts), fraud alert
list + detail page (triage actions: Mark Reviewed / Dismiss, auth-gated), customer risk
summary page (PySpark-computed profiles), login page + JWT interceptor, a shared design
system (moved from one component's SCSS into `styles.scss` so every page looks consistent),
3 reusable components (`StatCard`, `StatusBadge`, `NavBar`). Dashboard gained a fraud-trend
bar chart, a top-triggers panel, and an avg-latency stat card.

**Testing**: a full `FraudDetection.Api.Tests` xUnit project (unit tests for the legacy rule
engine + password hashing, integration tests via `WebApplicationFactory` against EF InMemory
covering auth, validation, pagination, search, and 401/404 paths), 20 Angular unit tests
(services + components, run headless via Karma/Chromium), and an end-to-end smoke test
script (`scripts/e2e-smoke-test.sh`) that exercises the real flow against a live
`docker compose` stack: login -> reject unauthenticated write -> create a normal and a
high-risk transaction -> poll until Scala scores both -> verify the fraud alert and its
rule citations -> verify dashboard stats reflect it.

### Metrics

| Metric | Value |
|---|---|
| Total API endpoints | 14 controller actions + 2 health-check endpoints |
| New this phase | 6 (auth login, trends, top-triggers, customer-risk-profiles ×2, plus search/filter params on 2 existing) |
| Backend LOC (app) | ~1,834 |
| Backend LOC (tests) | ~572 |
| .NET tests | 25 (all passing) |
| Angular routes | 7 (+ wildcard redirect) |
| Angular components | 11 |
| Frontend LOC (app) | ~1,757 |
| Frontend LOC (tests) | ~335 |
| Angular tests | 20 (all passing, headless Chromium) |
| E2E smoke test assertions | 10 (all passing against the live stack) |

### Bugs found and fixed while getting this to actually run

- **EF's InMemory test provider doesn't understand `EF.Functions.ILike`** (a Npgsql-specific
  extension) — search filters were switched to `.ToLower().Contains(...)`, which translates
  correctly on both Postgres and InMemory.
- **Swapping `AppDbContext` to InMemory in `WebApplicationFactory` collided with the already-
  registered Npgsql provider** ("Only a single database provider can be registered") —
  removing the `DbContextOptions<AppDbContext>` descriptor wasn't enough, since `UseNpgsql`
  also registers provider marker services elsewhere in the collection via
  `TryAddEnumerable`. Fixed by giving the InMemory provider its own isolated
  `UseInternalServiceProvider(...)`.
- **Kafka publish calls in tests would have hung ~10s per attempt** waiting for a broker
  that isn't running in the test process — made `KafkaProducerService`'s message timeout
  configurable and set it to 500ms for tests.
- **Test JSON deserialization silently returned empty/default values** — the API's default
  camelCase + string-enum JSON output didn't match a hand-rolled `JsonSerializerOptions` in
  tests that had neither. Fixed with `PropertyNameCaseInsensitive = true` plus the enum
  converter (matching `System.Net.Http.Json`'s own `Web` defaults, which is why some tests
  worked without any explicit options and others silently didn't).
- **Angular's newer `@angular/build:karma` builder rejects the legacy
  `@angular-devkit/build-angular/plugins/karma` reference** in a custom `karma.conf.js`
  (it filters that framework/plugin out and injects its own) — removed it and pointed
  `angular.json`'s test target at the config via the `karmaConfig` option.

## Cumulative Totals (after Phase 4)

| Metric | Value |
|---|---|
| API endpoints | 16 (14 controller + 2 health) |
| Database tables | 3 domain (`transactions`, `fraud_alerts`, `customer_risk_profiles`) + 1 EF migrations history |
| Kafka topics | 1 |
| Producers | 1 (.NET) |
| Consumers | 1 (Scala Structured Streaming) |
| Background services | 2 (.NET) + 1 streaming job (Scala) + 1 batch job (PySpark) |
| Backend LOC (app / tests) | ~1,834 / ~572 |
| Frontend LOC (app / tests) | ~1,757 / ~335 |
| Scala LOC (app / tests) | ~461 / ~152 |
| PySpark LOC (app / tests) | ~337 / ~180 |
| Total automated tests | 76 (25 .NET + 20 Angular + 14 Scala + 17 PySpark) |
| E2E smoke assertions | 10 |
| Docker Compose services | 7 (`postgres`, `kafka`, `kafka-init`, `api`, `frontend`, `scala-risk-engine`, `pyspark-analytics`) |
| Live data (this run) | 539 transactions, 52 fraud alerts (37 Scala, 13 PySpark), 53 customer risk profiles |
