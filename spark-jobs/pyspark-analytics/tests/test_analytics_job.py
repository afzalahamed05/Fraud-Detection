import sys
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path

import pytest
from pyspark.sql import SparkSession

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from analytics_job import compute_customer_profiles, detect_amount_anomalies


@pytest.fixture(scope="module")
def spark():
    session = (
        SparkSession.builder.master("local[1]")
        .appName("pyspark-analytics-tests")
        .config("spark.ui.enabled", "false")
        .getOrCreate()
    )
    yield session
    session.stop()


def _tx(spark, account_id, amount, category="Dining", country="US", days_ago=0):
    occurred = datetime.now(timezone.utc) - timedelta(days=days_ago)
    return (account_id, Decimal(str(amount)), category, country, occurred)


def _transactions_df(spark, rows):
    return spark.createDataFrame(
        rows, schema="AccountId string, Amount decimal(18,2), MerchantCategory string, CountryCode string, OccurredAtUtc timestamp"
    )


def test_compute_customer_profiles_basic_stats(spark):
    rows = [
        _tx(spark, "acct-1", 100, days_ago=10),
        _tx(spark, "acct-1", 200, days_ago=5),
        _tx(spark, "acct-1", 150, days_ago=0),
    ]
    df = _transactions_df(spark, rows)

    profiles = compute_customer_profiles(df).collect()
    assert len(profiles) == 1

    profile = profiles[0]
    assert profile["AccountId"] == "acct-1"
    assert profile["TransactionCount"] == 3
    assert float(profile["AvgAmount"]) == pytest.approx(150.0, abs=0.01)
    assert profile["DistinctMerchantCategories"] == 1


def test_compute_customer_profiles_distinct_categories_and_countries(spark):
    rows = [
        _tx(spark, "acct-2", 50, category="Dining", country="US"),
        _tx(spark, "acct-2", 60, category="Retail", country="US"),
        _tx(spark, "acct-2", 70, category="Retail", country="CA"),
    ]
    df = _transactions_df(spark, rows)

    profile = compute_customer_profiles(df).collect()[0]
    assert profile["DistinctMerchantCategories"] == 2
    assert profile["DistinctCountries"] == 2


def test_detect_amount_anomalies_flags_statistical_outlier(spark):
    account_id = "acct-3"
    # 20 normal transactions clustered $40-$59 give a tight, meaningful baseline. A small
    # history (e.g. 6 rows) isn't enough here: the outlier itself would dominate the mean/
    # stddev it's being compared against and end up looking "less anomalous" -- a real
    # limitation of naive z-scoring on small samples, avoided in production by requiring
    # MIN_TRANSACTION_HISTORY before trusting a profile at all.
    history_rows = [
        (uuid_str(i), account_id, Decimal(str(40 + i)), "Dining", "US", "Approved")
        for i in range(20)
    ]
    # ...then one wildly out-of-pattern $5000 transaction.
    outlier_row = (uuid_str(99), account_id, Decimal("5000.00"), "Dining", "US", "Approved")

    tx_df = spark.createDataFrame(
        history_rows + [outlier_row],
        schema="Id string, AccountId string, Amount decimal(18,2), MerchantCategory string, CountryCode string, Status string",
    ).withColumn("OccurredAtUtc", _now_col(spark))

    profiles = compute_customer_profiles(
        tx_df.withColumn("OccurredAtUtc", _now_col(spark))
    )

    anomalies = detect_amount_anomalies(tx_df, profiles).collect()
    flagged_ids = {row["TransactionId"] for row in anomalies}

    assert uuid_str(99) in flagged_ids
    assert uuid_str(0) not in flagged_ids


def uuid_str(i: int) -> str:
    return f"00000000-0000-0000-0000-{i:012d}"


def _now_col(spark):
    from pyspark.sql import functions as F

    return F.current_timestamp()
