# Resume Bullets

Every number below is pulled directly from [PROJECT_METRICS.md](PROJECT_METRICS.md) — measured
this session against a clean `docker compose up -d --build`, not estimated.

1. Built a real-time fraud detection pipeline using .NET 9, Apache Kafka, and Scala Spark
   Structured Streaming, processing transactions at 23.7 req/sec ingestion throughput with
   3–25ms of actual per-transaction scoring compute time, verified via 10 passing end-to-end
   assertions against a live Docker Compose stack.

2. Implemented a 7-rule fraud detection engine (6 configurable deterministic rules in Scala,
   1 statistical z-score anomaly detector in PySpark) achieving 100% detection and 0% false
   positives on a 40-transaction labeled test set, with customer behavioral baselines
   recomputed by PySpark feeding back into Scala's real-time scoring.

3. Wrote and maintained 76 automated tests (xUnit, Jasmine/Karma, ScalaTest, pytest) across a
   4-language stack (.NET, Angular, Scala, Python) with a 100% pass rate, including
   integration tests against an in-memory EF Core provider and headless-Chromium Angular
   component tests.

4. Containerized a 7-service architecture (.NET API, Angular, PostgreSQL, Kafka, Scala Spark,
   PySpark) with Docker Compose, cutting cold-start time to 27–43 seconds from a wiped
   environment to a fully healthy API, and eliminated a startup race condition by adding
   healthcheck-gated service dependencies.

5. Designed a 16-endpoint REST API in ASP.NET Core with JWT authentication, centralized
   exception handling, and Serilog structured logging, backing an Angular 20 dashboard (11
   components, 7 routes) that visualizes fraud trends, per-transaction pipeline traces, and
   risk-scoring provenance across ~4,200 lines of application code.
