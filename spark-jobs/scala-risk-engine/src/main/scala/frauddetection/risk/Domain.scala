package frauddetection.risk

import java.time.Instant

/** Mirrors the payload of FraudDetection.Api.Messaging.TransactionCreatedEventV1. */
case class TransactionEvent(
    transactionId: String,
    accountId: String,
    merchantName: String,
    merchantCategory: String,
    amount: BigDecimal,
    currency: String,
    countryCode: String,
    occurredAtUtc: Instant
)

/** Behavioral baseline computed by the PySpark analytics job, read from customer_risk_profiles. */
case class CustomerProfile(
    accountId: String,
    transactionCount: Int,
    avgAmount: BigDecimal,
    stdDevAmount: BigDecimal,
    maxAmount: BigDecimal,
    avgTransactionsPerDay: Double
)

/** Everything a rule needs to decide, gathered once per transaction before rule evaluation. */
case class RuleContext(
    transaction: TransactionEvent,
    recentTransactionCountInWindow: Int,
    todayTransactionCount: Int,
    profile: Option[CustomerProfile]
)

case class RuleResult(ruleName: String, triggered: Boolean, points: Int, reason: String)

case class RiskAssessment(
    transactionId: String,
    riskScore: Int,
    flagged: Boolean,
    triggeredRules: List[String],
    reasons: List[String]
)
