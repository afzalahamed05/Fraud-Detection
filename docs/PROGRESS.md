# Project Progress

## Phase 1 — Foundation ✅ COMPLETE

Monorepo with ASP.NET Core Web API, Angular dashboard, PostgreSQL, EF Core migrations,
a deterministic rule-based fraud scorer, and Docker Compose orchestration. Verified running
end-to-end (Docker Compose + browser-driven check against the live API).

### Metrics (resume-trackable)

| Metric | Value |
|---|---|
| REST API endpoints | 6 |
| Database tables (domain) | 2 (`transactions`, `fraud_alerts`) |
| EF Core migrations | 1 (`InitialCreate`) |
| Backend LOC (C#) | ~675 |
| Frontend LOC (TS/HTML/SCSS) | ~444 |
| Frontend components | 2 (`App` shell, `DashboardComponent`) |
| Seed transactions generated | 500 random + 6 velocity-burst accounts (~530 total) |
| Fraud detection rules | 3 (amount threshold, high-risk country, velocity) |
| Docker Compose services | 3 (`postgres`, `api`, `frontend`) |
| Manual verification | API endpoints (curl), DB restart idempotency, browser render of dashboard against live API, zero console errors |

### Endpoints

- `POST /api/transactions` — create a transaction, runs fraud scoring synchronously
- `GET /api/transactions` — paginated list, filterable by status
- `GET /api/transactions/{id}`
- `GET /api/fraud-alerts` — paginated list, filterable by severity/status
- `PATCH /api/fraud-alerts/{id}/status` — triage an alert (Open/Reviewed/Dismissed)
- `GET /api/dashboard/stats` — counts, fraud rate, volume, alerts by severity

## Phase 2 — Real-Time Event Streaming ✅ COMPLETE

Kafka added between the API and fraud scoring: `POST /api/transactions` now persists as
`Pending` and publishes to `transactions.created`; `TransactionConsumerService` consumes it,
runs the same `FraudDetectionService` from Phase 1, and writes the result back. Reliability:
inline publish retry, an outbox sweep for anything that fails to publish, manual offset
commits (only after the DB write succeeds), and an idempotent consumer (verified live against
532 pre-existing rows with zero duplicate alerts). Angular polls every 3s and has a
"Simulate Transaction" button so the Pending → resolved transition is visible without a
manual refresh. Full metrics and test results: [PROJECT_METRICS.md](../PROJECT_METRICS.md).

## Phase 3 — Fraud Detection Engine: Scala + PySpark ✅ COMPLETE

Real intelligence layer, replacing the Phase 1/2 synchronous C# rule engine (kept in the repo,
unregistered, for reference). A Scala Spark Structured Streaming job consumes
`transactions.created` directly, scores every transaction against 6 configurable deterministic
rules, and writes results straight to Postgres via JDBC. A PySpark batch job runs every 30s,
recomputing per-customer behavioral baselines (`customer_risk_profiles`) that feed back into
two of Scala's rules, and independently flags statistical z-score outliers PySpark's own way —
genuinely different techniques on different time horizons, not duplicated logic. 14 Scala unit
tests (pure functions, no SparkSession) and 17 PySpark tests (pytest, local SparkSession for
aggregation logic) all pass. Full metrics, the architecture diagram, and the bugs found getting
it to actually run: [PROJECT_METRICS.md](../PROJECT_METRICS.md).

## Phase 4 — Portfolio-Quality Application ✅ COMPLETE

JWT authentication (protects writes, GETs stay public), centralized error handling, Serilog
structured logging, ASP.NET Core health checks, Swagger with auth + XML docs, and
search/filter/trends/top-triggers endpoints on the .NET side. Angular gained real routing,
transaction/alert detail pages with full pipeline traces, a customer risk summary page, a
login flow, and dashboard trend/top-triggers panels — all reusing a shared component/style
system rather than one-off pages. Added a full .NET xUnit suite (25 tests), 20 Angular unit
tests (headless Chromium), and an end-to-end smoke test script exercising the real
API → Kafka → Scala → Postgres flow against the live Docker Compose stack. Full metrics and
the bugs found: [PROJECT_METRICS.md](../PROJECT_METRICS.md).

## Final Audit — Verified & Resume-Ready ✅ COMPLETE

Full clean-environment verification (`docker compose down -v` then `up -d --build`) of every
component, all 4 test suites (76 tests) and the E2E smoke test rerun fresh, 100% pass rate.
Found and fixed a real startup race condition (PySpark's first run failed on a cold start
because it didn't wait for the API's migration — fixed with a proper Docker healthcheck),
fixed a committed-plaintext JWT secret (moved to a gitignored `.env`, fail-fast if missing),
and removed genuinely dead code (`TransactionConsumerService.cs`). Measured real throughput
(23.7 req/s), latency (avg/p95 via Postgres timestamps), and fraud detection rate (100%/0%
false-positive on a 40-transaction labeled test set) rather than estimating any of them.
[PROJECT_METRICS.md](../PROJECT_METRICS.md) has the full table + methodology,
[RESUME_BULLETS.md](../RESUME_BULLETS.md) and [INTERVIEW_NOTES.md](../INTERVIEW_NOTES.md)
turn it into resume/interview material.

## Phase 5 — Production hardening (not started)

- Rate limiting
- CI pipeline
- Real user management (beyond the single seeded demo admin)
- Dead-letter topic for poison Kafka messages
