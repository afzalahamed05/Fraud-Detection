"""All thresholds are environment-overridable so tuning never needs a code change."""
import os


def _float_env(name: str, default: float) -> float:
    return float(os.environ.get(name, default))


def _int_env(name: str, default: int) -> int:
    return int(os.environ.get(name, default))


POSTGRES_URL = os.environ.get("POSTGRES_JDBC_URL", "jdbc:postgresql://postgres:5432/frauddetection")
POSTGRES_USER = os.environ.get("POSTGRES_USER", "postgres")
POSTGRES_PASSWORD = os.environ.get("POSTGRES_PASSWORD", "postgres")

# Separate host/port/db for psycopg2 (used only for writes -- see the note in analytics_job.py
# on why writes don't go through Spark's generic DataFrame.write.jdbc()).
POSTGRES_HOST = os.environ.get("POSTGRES_HOST", "postgres")
POSTGRES_PORT = _int_env("POSTGRES_PORT", 5432)
POSTGRES_DB = os.environ.get("POSTGRES_DB", "frauddetection")

# A customer needs at least this many historical transactions before we trust their
# average/stddev enough to call anything "anomalous" relative to it.
MIN_TRANSACTION_HISTORY = _int_env("MIN_TRANSACTION_HISTORY", 5)

# |z-score| beyond this is flagged as a statistical outlier for that specific customer.
Z_SCORE_THRESHOLD = _float_env("Z_SCORE_THRESHOLD", 3.0)

# Converts |z-score| into a 0-100 risk score: risk = min(100, |z| * this).
Z_SCORE_TO_RISK_MULTIPLIER = _float_env("Z_SCORE_TO_RISK_MULTIPLIER", 15.0)

ANALYTICS_INTERVAL_SECONDS = _int_env("ANALYTICS_INTERVAL_SECONDS", 30)
