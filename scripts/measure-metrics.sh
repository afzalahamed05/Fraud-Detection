#!/usr/bin/env bash
# Measures real throughput, latency percentiles, and fraud-detection rate against a live
# docker-compose stack. Every number this script prints is read back from the database or
# timed directly -- nothing here is estimated.
set -euo pipefail

API="${API_BASE_URL:-http://localhost:5274/api}"
PG_CONTAINER="${PG_CONTAINER:-upgrade-your-brain-postgres-1}"
BATCH_TAG="metrics-run-$(date +%s)"

gen_uuid() {
  local hex; hex=$(openssl rand -hex 16)
  echo "${hex:0:8}-${hex:8:4}-4${hex:13:3}-a${hex:17:3}-${hex:20:12}"
}

echo "== Logging in =="
TOKEN=$(curl -sf -X POST "$API/auth/login" -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' | grep -oE '"token":"[^"]+"' | cut -d'"' -f4)

echo "== Submitting labeled test dataset (batch tag: $BATCH_TAG) =="
SUBMIT_START=$(date +%s.%N)
# 20 unambiguous fraud cases: very large amount + high-risk country -> VeryLargeAmount (45) +
# RiskyCountry (20) = 65, always >= the 40-point flag threshold.
FRAUD_COUNT=20
for i in $(seq 1 $FRAUD_COUNT); do
  curl -sf -X POST "$API/transactions" -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
    -d "{\"accountId\":\"$(gen_uuid)\",\"merchantName\":\"$BATCH_TAG-fraud-$i\",\"merchantCategory\":\"Retail\",\"amount\":12000,\"currency\":\"USD\",\"countryCode\":\"KP\"}" > /dev/null &
done

# 20 unambiguous normal cases: small amount, trusted country -> 0 points, never flagged.
NORMAL_COUNT=20
for i in $(seq 1 $NORMAL_COUNT); do
  curl -sf -X POST "$API/transactions" -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
    -d "{\"accountId\":\"$(gen_uuid)\",\"merchantName\":\"$BATCH_TAG-normal-$i\",\"merchantCategory\":\"Dining\",\"amount\":15,\"currency\":\"USD\",\"countryCode\":\"US\"}" > /dev/null &
done

wait
SUBMIT_END=$(date +%s.%N)
TOTAL_SUBMITTED=$((FRAUD_COUNT + NORMAL_COUNT))
awk -v start="$SUBMIT_START" -v end="$SUBMIT_END" -v total="$TOTAL_SUBMITTED" 'BEGIN {
  seconds = end - start
  printf "  Submitted %d transactions in %.2fs (%.2f req/s, concurrent POSTs).\n", total, seconds, total/seconds
}'

echo "== Waiting for Scala to process all of them (up to 30s) =="
for attempt in $(seq 1 15); do
  PENDING=$(docker exec "$PG_CONTAINER" psql -U postgres -d frauddetection -t -c \
    "SELECT COUNT(*) FROM transactions WHERE \"MerchantName\" LIKE '$BATCH_TAG%' AND \"Status\" = 'Pending';" | tr -d ' ')
  if [[ "$PENDING" == "0" ]]; then break; fi
  sleep 2
done

echo "== Results (queried directly from Postgres) =="
docker exec "$PG_CONTAINER" psql -U postgres -d frauddetection -c "
SELECT
  COUNT(*) AS total,
  COUNT(*) FILTER (WHERE \"MerchantName\" LIKE '$BATCH_TAG-fraud-%')  AS fraud_submitted,
  COUNT(*) FILTER (WHERE \"MerchantName\" LIKE '$BATCH_TAG-fraud-%' AND \"Status\" = 'Flagged') AS fraud_detected,
  COUNT(*) FILTER (WHERE \"MerchantName\" LIKE '$BATCH_TAG-normal-%') AS normal_submitted,
  COUNT(*) FILTER (WHERE \"MerchantName\" LIKE '$BATCH_TAG-normal-%' AND \"Status\" = 'Flagged') AS normal_false_positives
FROM transactions WHERE \"MerchantName\" LIKE '$BATCH_TAG%';
"

echo "== Processing latency (PublishedToKafkaUtc -> ProcessedAtUtc), this batch, milliseconds =="
docker exec "$PG_CONTAINER" psql -U postgres -d frauddetection -c "
SELECT
  COUNT(*) AS processed_count,
  ROUND(AVG(EXTRACT(EPOCH FROM (\"ProcessedAtUtc\" - \"PublishedToKafkaUtc\")) * 1000)::numeric, 1) AS avg_ms,
  ROUND(PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (\"ProcessedAtUtc\" - \"PublishedToKafkaUtc\")) * 1000)::numeric, 1) AS p95_ms,
  ROUND(MIN(EXTRACT(EPOCH FROM (\"ProcessedAtUtc\" - \"PublishedToKafkaUtc\")) * 1000)::numeric, 1) AS min_ms,
  ROUND(MAX(EXTRACT(EPOCH FROM (\"ProcessedAtUtc\" - \"PublishedToKafkaUtc\")) * 1000)::numeric, 1) AS max_ms
FROM transactions
WHERE \"MerchantName\" LIKE '$BATCH_TAG%' AND \"ProcessedAtUtc\" IS NOT NULL;
"

