"""
PySpark batch analytics job. Runs periodically (see main loop at the bottom), reads all
transactions from Postgres, and produces two things:

1. customer_risk_profiles -- per-account behavioral baseline (count, avg/stddev amount,
   distinct categories/countries, avg transactions/day). Read by the Scala risk engine
   for its SpendingEscalation and UnusualFrequency rules.
2. Statistical anomaly alerts -- transactions whose amount is a z-score outlier relative
   to *that specific customer's* history, or that used a merchant category/country never
   seen before for that customer. Written to fraud_alerts with Source=PySparkAnomalyDetection.
   This is deliberately not the same technique as the Scala engine's fixed thresholds:
   Scala catches "objectively large/risky", PySpark catches "unusual for this person".
"""
import logging
import time
import uuid

import psycopg2
import psycopg2.extras
from pyspark.sql import SparkSession, DataFrame
from pyspark.sql import functions as F
from pyspark.sql.types import IntegerType, StringType

import config
from rules import (
    compute_zscore,
    is_amount_anomaly,
    risk_score_from_zscore,
    severity_for_score,
)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)-5s %(name)s - %(message)s",
)
logger = logging.getLogger("pyspark-analytics")

JDBC_PROPERTIES = {
    "user": config.POSTGRES_USER,
    "password": config.POSTGRES_PASSWORD,
    "driver": "org.postgresql.Driver",
}


def build_spark() -> SparkSession:
    return (
        SparkSession.builder.appName("pyspark-analytics")
        .config("spark.jars", "/opt/spark-jars/postgresql-42.7.3.jar")
        .getOrCreate()
    )


def load_transactions(spark: SparkSession) -> DataFrame:
    return spark.read.jdbc(config.POSTGRES_URL, "transactions", properties=JDBC_PROPERTIES)


def compute_customer_profiles(transactions: DataFrame) -> DataFrame:
    """Interpretable, non-ML aggregate statistics -- no black box here."""
    first_last = transactions.groupBy("AccountId").agg(
        F.min("OccurredAtUtc").alias("first_seen"),
        F.max("OccurredAtUtc").alias("last_seen"),
    )

    agg = transactions.groupBy("AccountId").agg(
        F.count("*").alias("TransactionCount"),
        F.avg("Amount").alias("AvgAmount"),
        F.coalesce(F.stddev("Amount"), F.lit(0.0)).alias("StdDevAmount"),
        F.max("Amount").alias("MaxAmount"),
        F.countDistinct("MerchantCategory").alias("DistinctMerchantCategories"),
        F.countDistinct("CountryCode").alias("DistinctCountries"),
        F.max("OccurredAtUtc").alias("LastTransactionAtUtc"),
    ).join(first_last, on="AccountId")

    active_days = F.greatest(
        F.lit(1.0),
        (F.unix_timestamp("last_seen") - F.unix_timestamp("first_seen")) / F.lit(86400.0),
    )

    return agg.withColumn(
        "AvgTransactionsPerDay", (F.col("TransactionCount") / active_days).cast("decimal(10,2)")
    ).select(
        "AccountId", "TransactionCount",
        F.col("AvgAmount").cast("decimal(18,2)").alias("AvgAmount"),
        F.col("StdDevAmount").cast("decimal(18,2)").alias("StdDevAmount"),
        F.col("MaxAmount").cast("decimal(18,2)").alias("MaxAmount"),
        "DistinctMerchantCategories", "DistinctCountries",
        "AvgTransactionsPerDay", "LastTransactionAtUtc",
    ).withColumn("UpdatedAtUtc", F.current_timestamp())


def pg_connect():
    return psycopg2.connect(
        host=config.POSTGRES_HOST,
        port=config.POSTGRES_PORT,
        dbname=config.POSTGRES_DB,
        user=config.POSTGRES_USER,
        password=config.POSTGRES_PASSWORD,
    )


def write_profiles(profiles: DataFrame) -> int:
    """Full recompute each run -- customer_risk_profiles is a derived table, safe to rebuild
    from scratch. Written via psycopg2 rather than DataFrame.write.jdbc(): Spark's generic
    JDBC writer has no way to know AccountId is a Postgres `uuid` column (it reads/writes
    it as a plain string), so a bulk write fails with "column is of type uuid but expression
    is of type character varying". Explicit ::uuid casts in raw SQL sidestep that; row counts
    here (one per account) are small enough that collecting to the driver is fine.
    """
    rows = profiles.collect()
    if not rows:
        return 0

    conn = pg_connect()
    try:
        with conn.cursor() as cur:
            cur.execute("TRUNCATE TABLE customer_risk_profiles")
            psycopg2.extras.execute_values(
                cur,
                """INSERT INTO customer_risk_profiles
                   ("AccountId", "TransactionCount", "AvgAmount", "StdDevAmount", "MaxAmount",
                    "DistinctMerchantCategories", "DistinctCountries", "AvgTransactionsPerDay",
                    "LastTransactionAtUtc", "UpdatedAtUtc")
                   VALUES %s""",
                [
                    (
                        r["AccountId"], r["TransactionCount"], r["AvgAmount"], r["StdDevAmount"],
                        r["MaxAmount"], r["DistinctMerchantCategories"], r["DistinctCountries"],
                        r["AvgTransactionsPerDay"], r["LastTransactionAtUtc"], r["UpdatedAtUtc"],
                    )
                    for r in rows
                ],
                template="(%s::uuid, %s, %s, %s, %s, %s, %s, %s, %s, %s)",
            )
        conn.commit()
    finally:
        conn.close()
    return len(rows)


def detect_amount_anomalies(transactions: DataFrame, profiles: DataFrame) -> DataFrame:
    """Only judges customers with enough history to have a meaningful baseline, and only
    transactions already scored by the Scala engine (Status != Pending) to avoid racing it.

    UDFs are created here (not at module import time) because F.udf() needs an active
    SparkContext -- creating them at import time breaks pytest collection, which imports
    this module before any SparkSession fixture has run.
    """
    # compute_zscore/risk_score_from_zscore are plain-float functions, but Amount/AvgAmount/
    # StdDevAmount are Spark `decimal` columns -- those arrive in the UDF as python
    # decimal.Decimal, which silently fails to coerce into a declared "double" return type
    # (the UDF just returns null). Casting to double before the UDF call avoids that.
    zscore_udf = F.udf(lambda a, avg, std: compute_zscore(a, avg, std), "double")
    risk_udf = F.udf(
        lambda z: risk_score_from_zscore(z, config.Z_SCORE_TO_RISK_MULTIPLIER), IntegerType()
    )
    severity_udf = F.udf(lambda score: severity_for_score(score), StringType())

    eligible_profiles = profiles.filter(F.col("TransactionCount") >= config.MIN_TRANSACTION_HISTORY)

    joined = (
        transactions.filter(F.col("Status") != "Pending")
        .join(eligible_profiles, on="AccountId")
        .withColumn(
            "zscore",
            zscore_udf(
                F.col("Amount").cast("double"),
                F.col("AvgAmount").cast("double"),
                F.col("StdDevAmount").cast("double"),
            ),
        )
    )

    anomalies = joined.filter(
        F.col("zscore").isNotNull() & (F.abs(F.col("zscore")) > F.lit(config.Z_SCORE_THRESHOLD))
    )

    return (
        anomalies.withColumn("RiskScore", risk_udf(F.col("zscore")))
        .withColumn("Severity", severity_udf(F.col("RiskScore")))
        .withColumn(
            "Reason",
            F.concat(
                F.lit("Amount is "), F.round(F.abs(F.col("zscore")), 1),
                F.lit(" std deviations from this account's historical average ($"),
                F.round(F.col("AvgAmount"), 2), F.lit(")"),
            ),
        )
        .select(
            F.col("Id").alias("TransactionId"),
            F.col("RiskScore").cast("decimal(5,2)").alias("RiskScore"),
            "Severity", "Reason",
        )
    )


def write_anomaly_alerts(spark: SparkSession, anomalies: DataFrame) -> int:
    if anomalies.rdd.isEmpty():
        return 0

    existing = spark.read.jdbc(config.POSTGRES_URL, "fraud_alerts", properties=JDBC_PROPERTIES).filter(
        F.col("Source") == "PySparkAnomalyDetection"
    ).select("TransactionId")

    # Idempotency: never re-alert a transaction this job already flagged in a prior run.
    new_alerts = anomalies.join(existing, on="TransactionId", how="left_anti").collect()
    if not new_alerts:
        return 0

    conn = pg_connect()
    try:
        with conn.cursor() as cur:
            psycopg2.extras.execute_values(
                cur,
                """INSERT INTO fraud_alerts
                   ("Id", "TransactionId", "RiskScore", "Severity", "Status",
                    "Reason", "CreatedAtUtc", "Source", "TriggeredRules")
                   VALUES %s""",
                [
                    (
                        str(uuid.uuid4()), r["TransactionId"], r["RiskScore"], r["Severity"],
                        "Open", r["Reason"], "PySparkAnomalyDetection", '["StatisticalAmountAnomaly"]',
                    )
                    for r in new_alerts
                ],
                template="(%s::uuid, %s::uuid, %s, %s, %s, %s, now(), %s, %s)",
            )
        conn.commit()
    finally:
        conn.close()
    return len(new_alerts)


def run_once() -> None:
    started = time.time()
    spark = build_spark()
    try:
        transactions = load_transactions(spark).cache()
        total = transactions.count()
        logger.info("event=job_start total_transactions=%d", total)

        profiles = compute_customer_profiles(transactions)
        profile_count = write_profiles(profiles)

        anomalies = detect_amount_anomalies(transactions, profiles)
        alert_count = write_anomaly_alerts(spark, anomalies)

        elapsed = time.time() - started
        logger.info(
            "event=job_complete total_transactions=%d profiles_written=%d "
            "new_anomaly_alerts=%d elapsedSeconds=%.2f",
            total, profile_count, alert_count, elapsed,
        )
    finally:
        transactions.unpersist()
        spark.stop()


if __name__ == "__main__":
    while True:
        try:
            run_once()
        except Exception:
            logger.exception("event=job_failed")
        time.sleep(config.ANALYTICS_INTERVAL_SECONDS)
