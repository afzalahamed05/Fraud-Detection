import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from rules import (
    avg_transactions_per_day,
    compute_zscore,
    is_amount_anomaly,
    risk_score_from_zscore,
    severity_for_score,
)


def test_zscore_none_when_stddev_zero():
    assert compute_zscore(100, 50, 0) is None


def test_zscore_none_when_stddev_negative():
    assert compute_zscore(100, 50, -5) is None


def test_zscore_positive_for_amount_above_average():
    assert compute_zscore(150, 100, 25) == 2.0


def test_zscore_negative_for_amount_below_average():
    assert compute_zscore(50, 100, 25) == -2.0


def test_is_amount_anomaly_true_above_threshold():
    assert is_amount_anomaly(3.5, 3.0) is True


def test_is_amount_anomaly_false_within_threshold():
    assert is_amount_anomaly(1.2, 3.0) is False


def test_is_amount_anomaly_false_for_none():
    assert is_amount_anomaly(None, 3.0) is False


def test_is_amount_anomaly_symmetric_for_negative_zscore():
    assert is_amount_anomaly(-4.0, 3.0) is True


def test_risk_score_from_zscore_scales_with_magnitude():
    assert risk_score_from_zscore(2.0, 15.0) == 30
    assert risk_score_from_zscore(4.0, 15.0) == 60


def test_risk_score_from_zscore_caps_at_100():
    assert risk_score_from_zscore(50.0, 15.0) == 100


def test_risk_score_from_zscore_none_is_zero():
    assert risk_score_from_zscore(None, 15.0) == 0


def test_severity_buckets():
    assert severity_for_score(10) == "Medium"
    assert severity_for_score(45) == "Medium"
    assert severity_for_score(60) == "High"
    assert severity_for_score(79) == "High"
    assert severity_for_score(80) == "Critical"
    assert severity_for_score(100) == "Critical"


def test_avg_transactions_per_day_normal_case():
    assert avg_transactions_per_day(30, 15.0) == 2.0


def test_avg_transactions_per_day_clamps_new_accounts_to_one_day():
    # 5 transactions "today" for a brand new account should not read as an absurd rate
    assert avg_transactions_per_day(5, 0.1) == 5.0
