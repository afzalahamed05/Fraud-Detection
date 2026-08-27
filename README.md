# Real-Time Fraud Detection & Risk Analytics Platform

A fraud detection platform where transaction scoring actually happens on a streaming
pipeline, not inline in an API call. An ASP.NET Core API durably persists each transaction
and publishes it to Kafka; a Scala Spark Structured Streaming engine scores it in real time
against 6 configurable rules; a PySpark batch job independently recomputes customer
behavioral baselines and flags statistical anomalies the fixed rules can't see. Everything
is verified end-to-end and every number below is measured, not estimated — see
[PROJECT_METRICS.md](PROJECT_METRICS.md) for methodology.

## Architecture

```
Angular 20  <-->  ASP.NET Core API (JWT auth, EF Core, Serilog)
                        |
              POST /api/transactions
                        |
        Postgres insert (Status=Pending)   <- durable before Kafka is even touched
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
           - recomputes customer_risk_profiles from full history
           - z-score anomaly detection -> FraudAlert (Source=PySparkAnomalyDetection)
                        |
              .NET API reads it all back out
                        |
              Angular polls every 3s, renders it
```

Postgres is the single source of truth every service reads from and writes to — Kafka is
the transport for the real-time hop, not a system of record. Full design rationale and
tradeoffs: [INTERVIEW_NOTES.md](INTERVIEW_NOTES.md).

## Tech stack

**Backend**: C# / .NET 9, ASP.NET Core Web API, Entity Framework Core, PostgreSQL, Serilog, JWT auth, Swagger/OpenAPI, xUnit
**Frontend**: Angular 20, TypeScript, RxJS, Karma/Jasmine
**Streaming**: Apache Kafka, Scala, Apache Spark Structured Streaming, sbt, ScalaTest
**Analytics**: Python, PySpark, pytest
**Infra**: Docker, Docker Compose

## Measured results

| Metric | Value |
|---|---|
| Source LOC (app, 4 languages) | 4,231 |
| API endpoints | 16 |
| Fraud detection rules | 7 (6 Scala + 1 PySpark) |
| Automated tests | 76, 100% passing |
| API ingestion throughput | 23.7 req/sec |
| End-to-end latency (avg / p95) | 2,410ms / 5,972ms |
| Per-transaction Scala compute time | 3–25ms |
| Fraud detection rate (labeled test set) | 100%, 0% false positives |
| Cold start (wiped env → healthy API) | 27–43s |

Full table with exact methodology: [PROJECT_METRICS.md](PROJECT_METRICS.md).

## Quick start

```bash
cp .env.example .env   # first time only -- fill in a real AUTH_JWT_SECRET (see the file)
docker compose up -d --build
```

That's the whole stack: Postgres, Kafka, the .NET API, the Angular frontend, the Scala
Structured Streaming risk engine, and the PySpark analytics job.

- Frontend: http://localhost:4200 — demo login: `admin` / `admin123`
- API: http://localhost:5274 (Swagger at `/swagger`)
- Postgres: localhost:5435 (mapped to avoid colliding with other local Postgres instances)
- Kafka: internal only (`kafka:9092` inside the compose network), no host port published

## Verify it's actually working end-to-end

```bash
./scripts/e2e-smoke-test.sh
```

Logs in, creates a normal and a high-risk transaction, waits for the Scala engine to score
both, and checks the resulting fraud alert and dashboard stats — the real
API → Kafka → Scala → Postgres flow, not a mock.

```bash
./scripts/measure-metrics.sh
```

Submits a 40-transaction labeled test set (20 engineered as unambiguous fraud, 20 as
unambiguous normal), then reads throughput, latency percentiles, and detection rate straight
out of Postgres.

## Running the tests

```bash
# .NET (unit + integration, EF InMemory, no live DB needed)
docker run --rm -v "$(pwd)/backend:/src" -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet test FraudDetection.Api.Tests/FraudDetection.Api.Tests.csproj

# Angular (headless Chromium)
cd frontend && npm test -- --watch=false

# Scala (pure rule-engine unit tests, no Spark/Kafka needed)
cd spark-jobs/scala-risk-engine && sbt test

# PySpark (pytest, one test spins up a local SparkSession)
docker build -t pyspark-analytics:latest spark-jobs/pyspark-analytics
docker run --rm --entrypoint pytest pyspark-analytics:latest -v tests/
```

## Project structure

```
backend/
  FraudDetection.Api/          ASP.NET Core Web API (controllers, EF Core, Kafka producer)
  FraudDetection.Api.Tests/    xUnit unit + integration tests
frontend/
  src/app/
    core/                      services, models, HTTP interceptor
    features/                  dashboard, transactions, alerts, customers, login
    shared/components/         reusable stat-card, status-badge, nav-bar
spark-jobs/
  scala-risk-engine/           Spark Structured Streaming risk engine (sbt project)
  pyspark-analytics/           PySpark batch analytics + anomaly detection
scripts/
  e2e-smoke-test.sh            end-to-end pipeline verification
  measure-metrics.sh           throughput/latency/detection-rate measurement
docs/PROGRESS.md               phase-by-phase build log
docker-compose.yml             the whole stack, one command
```

## Kafka / Spark debugging

```bash
# list topics
docker exec upgrade-your-brain-kafka-1 /opt/kafka/bin/kafka-topics.sh --list --bootstrap-server localhost:9092

# tail raw messages
docker exec upgrade-your-brain-kafka-1 /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 --topic transactions.created --from-beginning

# Scala risk engine logs (structured: event=risk_scored / event=batch_complete)
docker logs -f upgrade-your-brain-scala-risk-engine-1

# PySpark analytics job logs (runs every 30s)
docker logs -f upgrade-your-brain-pyspark-analytics-1

# pipeline health from the API's point of view
curl -s http://localhost:5274/api/health/pipeline

# ASP.NET Core health checks (Postgres + Kafka reachability)
curl -s http://localhost:5274/health/ready
```

Stop with:

```bash
docker compose down
```

Add `-v` to also drop the Postgres/Kafka volumes (wipes all data for a truly clean restart).

## Local dev loop (frontend only, against dockerized API)

```bash
docker compose up -d postgres api
cd frontend
npm install
npm start
```

Frontend dev server runs at http://localhost:4200 and talks to the API at
`http://localhost:5274/api` (see `src/app/core/services/api.service.ts`).

## Backend dev loop

Backend iteration goes through the SDK container if you don't have the .NET SDK installed
locally:

```bash
docker run --rm -v "$(pwd)/backend:/src" -w /src/FraudDetection.Api mcr.microsoft.com/dotnet/sdk:9.0 dotnet build
```

With the .NET 9 SDK installed locally, `dotnet watch run` from `backend/FraudDetection.Api`
gives a faster inner loop.

## More documentation

- [PROJECT_METRICS.md](PROJECT_METRICS.md) — full metrics table, exact measurement methodology, bugs found and fixed during audit
- [INTERVIEW_NOTES.md](INTERVIEW_NOTES.md) — architecture rationale, tradeoffs, scalability discussion, Q&A
- [RESUME_BULLETS.md](RESUME_BULLETS.md) — resume-ready bullets, every number sourced from the metrics table
- [docs/PROGRESS.md](docs/PROGRESS.md) — phase-by-phase build log
