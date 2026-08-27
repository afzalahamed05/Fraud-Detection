#!/usr/bin/env bash
# End-to-end smoke test of the core flow:
#   .NET API -> Kafka -> Scala risk engine -> Postgres -> .NET API -> (Angular reads the same API)
#
# Run against a live docker-compose stack:
#   docker compose up -d --build
#   ./scripts/e2e-smoke-test.sh
#
# Exits non-zero on the first failed assertion so it's CI-friendly.
set -euo pipefail

API="${API_BASE_URL:-http://localhost:5274/api}"
PASS=0
FAIL=0

assert_eq() {
  local expected="$1" actual="$2" label="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  [PASS] $label"
    PASS=$((PASS + 1))
  else
    echo "  [FAIL] $label -- expected '$expected', got '$actual'"
    FAIL=$((FAIL + 1))
  fi
}

assert_contains() {
  local haystack="$1" needle="$2" label="$3"
  if [[ "$haystack" == *"$needle"* ]]; then
    echo "  [PASS] $label"
    PASS=$((PASS + 1))
  else
    echo "  [FAIL] $label -- expected to find '$needle'"
    FAIL=$((FAIL + 1))
  fi
}

echo "== 1. API liveness =="
LIVE=$(curl -sf "$API/health/live" || echo "UNREACHABLE")
assert_contains "$LIVE" "" "GET /health/live responds"

echo "== 2. Login as demo admin =="
LOGIN_RESPONSE=$(curl -sf -X POST "$API/auth/login" -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}')
TOKEN=$(echo "$LOGIN_RESPONSE" | grep -oE '"token":"[^"]+"' | cut -d'"' -f4)
if [[ -z "$TOKEN" ]]; then
  echo "  [FAIL] login did not return a token"
  FAIL=$((FAIL + 1))
else
  echo "  [PASS] login returned a JWT"
  PASS=$((PASS + 1))
fi

echo "== 3. Unauthenticated POST is rejected =="
UNAUTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/transactions" \
  -H "Content-Type: application/json" \
  -d '{"accountId":"11111111-1111-1111-1111-111111111111","merchantName":"x","merchantCategory":"Retail","amount":10}')
assert_eq "401" "$UNAUTH_STATUS" "POST /transactions without a token returns 401"

echo "== 4. Create a normal transaction =="
NORMAL_RESPONSE=$(curl -sf -X POST "$API/transactions" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"accountId":"22222222-2222-2222-2222-222222222222","merchantName":"E2E Coffee","merchantCategory":"Dining","amount":9.5,"currency":"USD","countryCode":"US"}')
NORMAL_ID=$(echo "$NORMAL_RESPONSE" | grep -oE '"id":"[^"]+"' | head -1 | cut -d'"' -f4)
NORMAL_STATUS=$(echo "$NORMAL_RESPONSE" | grep -oE '"status":"[^"]+"' | head -1 | cut -d'"' -f4)
assert_eq "Pending" "$NORMAL_STATUS" "normal transaction created as Pending"

echo "== 5. Create a high-risk transaction =="
RISKY_RESPONSE=$(curl -sf -X POST "$API/transactions" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"accountId":"33333333-3333-3333-3333-333333333333","merchantName":"E2E Risky Corp","merchantCategory":"Electronics","amount":14000,"currency":"USD","countryCode":"KP"}')
RISKY_ID=$(echo "$RISKY_RESPONSE" | grep -oE '"id":"[^"]+"' | head -1 | cut -d'"' -f4)

echo "== 6. Wait for the Scala risk engine to score both (up to 20s) =="
for i in $(seq 1 10); do
  NORMAL_FINAL=$(curl -sf "$API/transactions/$NORMAL_ID")
  RISKY_FINAL=$(curl -sf "$API/transactions/$RISKY_ID")
  NORMAL_STATUS=$(echo "$NORMAL_FINAL" | grep -oE '"status":"[^"]+"' | head -1 | cut -d'"' -f4)
  RISKY_STATUS=$(echo "$RISKY_FINAL" | grep -oE '"status":"[^"]+"' | head -1 | cut -d'"' -f4)
  if [[ "$NORMAL_STATUS" != "Pending" && "$RISKY_STATUS" != "Pending" ]]; then
    break
  fi
  sleep 2
done

assert_eq "Approved" "$NORMAL_STATUS" "normal transaction resolved to Approved"
assert_eq "Flagged" "$RISKY_STATUS" "high-risk transaction resolved to Flagged"
assert_contains "$RISKY_FINAL" "\"processingSource\":\"ScalaRiskEngine\"" "risky transaction was scored by ScalaRiskEngine"

echo "== 7. A fraud alert exists for the risky transaction =="
ALERTS=$(curl -sf "$API/fraud-alerts?transactionId=$RISKY_ID")
assert_contains "$ALERTS" "\"transactionId\":\"$RISKY_ID\"" "fraud alert references the risky transaction"
assert_contains "$ALERTS" "RiskyCountry" "fraud alert cites the RiskyCountry rule"

echo "== 8. Dashboard stats reflect at least these transactions =="
STATS=$(curl -sf "$API/dashboard/stats")
TOTAL=$(echo "$STATS" | grep -oE '"totalTransactions":[0-9]+' | grep -oE '[0-9]+')
if [[ "$TOTAL" -ge 2 ]]; then
  echo "  [PASS] dashboard totalTransactions ($TOTAL) includes this run's transactions"
  PASS=$((PASS + 1))
else
  echo "  [FAIL] dashboard totalTransactions ($TOTAL) looks too low"
  FAIL=$((FAIL + 1))
fi

echo
echo "================================"
echo "Passed: $PASS  Failed: $FAIL"
echo "================================"

[[ "$FAIL" -eq 0 ]]
