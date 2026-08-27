"""Pure, Spark-free statistics helpers -- unit tested directly, no SparkSession needed.
analytics_job.py wraps these as UDFs for use inside DataFrame transformations."""
from typing import Optional


def compute_zscore(amount: float, avg_amount: float, stddev_amount: float) -> Optional[float]:
    """How many standard deviations `amount` is from this customer's historical average.
    None when stddev is 0/undefined (e.g. a customer with one distinct amount ever) --
    that's a "can't judge" signal, not a "definitely normal" one, so callers should not
    treat None as non-anomalous without checking transaction history length separately."""
    if stddev_amount is None or stddev_amount <= 0:
        return None
    return (amount - avg_amount) / stddev_amount


def is_amount_anomaly(zscore: Optional[float], threshold: float) -> bool:
    if zscore is None:
        return False
    return abs(zscore) > threshold


def risk_score_from_zscore(zscore: Optional[float], multiplier: float) -> int:
    if zscore is None:
        return 0
    return min(100, int(abs(zscore) * multiplier))


def severity_for_score(risk_score: int) -> str:
    if risk_score >= 80:
        return "Critical"
    if risk_score >= 60:
        return "High"
    return "Medium"


def avg_transactions_per_day(transaction_count: int, first_seen_days_ago: float) -> float:
    """first_seen_days_ago is how long the account has been observed; clamp to >= 1 day
    so a brand-new account with 2 transactions today doesn't compute an absurd rate."""
    active_days = max(1.0, first_seen_days_ago)
    return transaction_count / active_days
