package frauddetection.risk

import org.scalatest.flatspec.AnyFlatSpec
import org.scalatest.matchers.should.Matchers
import java.time.Instant

class RiskRulesSpec extends AnyFlatSpec with Matchers {

  val cfg: RiskRulesConfig = RiskRulesConfig(
    largeAmountThreshold = BigDecimal(5000),
    veryLargeAmountThreshold = BigDecimal(10000),
    velocityWindowSeconds = 600,
    velocityCountThreshold = 3,
    escalationMultiplier = 5.0,
    frequencyMultiplier = 3.0,
    riskyCountries = Set("RU", "NG", "KP", "IR", "BR"),
    riskyCategories = Set("Electronics", "Gaming"),
    riskyCategoryAmountThreshold = BigDecimal(3000),
    flagThreshold = 40,
    weights = RuleWeights(
      largeAmount = 25, veryLargeAmount = 45, riskyCountry = 20,
      velocity = 35, escalation = 30, frequency = 25, riskyCategory = 15
    )
  )

  private def tx(
      amount: BigDecimal = BigDecimal(50),
      country: String = "US",
      category: String = "Dining",
      accountId: String = "acct-1"
  ): TransactionEvent = TransactionEvent(
    transactionId = "tx-1",
    accountId = accountId,
    merchantName = "Test Merchant",
    merchantCategory = category,
    amount = amount,
    currency = "USD",
    countryCode = country,
    occurredAtUtc = Instant.parse("2026-01-01T00:00:00Z")
  )

  private def ctx(
      transaction: TransactionEvent = tx(),
      recentCount: Int = 0,
      todayCount: Int = 1,
      profile: Option[CustomerProfile] = None
  ): RuleContext = RuleContext(transaction, recentCount, todayCount, profile)

  // ---- Legitimate transactions ----

  "a small US transaction with no history" should "not be flagged" in {
    val assessment = RiskRules.evaluate(ctx(), cfg)
    assessment.flagged shouldBe false
    assessment.riskScore shouldBe 0
    assessment.triggeredRules shouldBe empty
  }

  "a transaction within a customer's normal spending pattern" should "not trigger escalation" in {
    val profile = CustomerProfile("acct-1", transactionCount = 20, avgAmount = BigDecimal(60),
      stdDevAmount = BigDecimal(15), maxAmount = BigDecimal(120), avgTransactionsPerDay = 2.0)
    val assessment = RiskRules.evaluate(ctx(transaction = tx(amount = BigDecimal(80)), profile = Some(profile)), cfg)
    assessment.flagged shouldBe false
    assessment.triggeredRules should not contain "SpendingEscalation"
  }

  // ---- Suspicious transactions ----

  "an amount over the large threshold" should "trigger LargeAmount" in {
    val result = RiskRules.largeAmount(ctx(tx(amount = BigDecimal(6000))), cfg)
    result.triggered shouldBe true
    result.ruleName shouldBe "LargeAmount"
    result.points shouldBe 25
  }

  "an amount over the very-large threshold" should "trigger VeryLargeAmount instead of LargeAmount" in {
    val result = RiskRules.largeAmount(ctx(tx(amount = BigDecimal(15000))), cfg)
    result.triggered shouldBe true
    result.ruleName shouldBe "VeryLargeAmount"
    result.points shouldBe 45
  }

  "a transaction from a high-risk country" should "trigger RiskyCountry" in {
    val result = RiskRules.riskyCountry(ctx(tx(country = "KP")), cfg)
    result.triggered shouldBe true
    result.points shouldBe 20
  }

  "3+ transactions in the velocity window" should "trigger HighVelocity" in {
    val result = RiskRules.velocity(ctx(recentCount = 4), cfg)
    result.triggered shouldBe true
    result.points shouldBe 35
  }

  "2 transactions in the velocity window" should "not trigger HighVelocity" in {
    val result = RiskRules.velocity(ctx(recentCount = 2), cfg)
    result.triggered shouldBe false
  }

  "an amount 5x+ a customer's historical average" should "trigger SpendingEscalation" in {
    val profile = CustomerProfile("acct-1", transactionCount = 10, avgAmount = BigDecimal(100),
      stdDevAmount = BigDecimal(20), maxAmount = BigDecimal(200), avgTransactionsPerDay = 1.5)
    val result = RiskRules.escalation(ctx(tx(amount = BigDecimal(600)), profile = Some(profile)), cfg)
    result.triggered shouldBe true
    result.points shouldBe 30
  }

  "escalation" should "not trigger for a brand-new account with under 3 transactions" in {
    val profile = CustomerProfile("acct-1", transactionCount = 1, avgAmount = BigDecimal(50),
      stdDevAmount = BigDecimal(0), maxAmount = BigDecimal(50), avgTransactionsPerDay = 1.0)
    val result = RiskRules.escalation(ctx(tx(amount = BigDecimal(1000)), profile = Some(profile)), cfg)
    result.triggered shouldBe false
  }

  "today's count exceeding the daily-average multiplier" should "trigger UnusualFrequency" in {
    val profile = CustomerProfile("acct-1", transactionCount = 30, avgAmount = BigDecimal(80),
      stdDevAmount = BigDecimal(10), maxAmount = BigDecimal(150), avgTransactionsPerDay = 2.0)
    val result = RiskRules.frequency(ctx(todayCount = 10, profile = Some(profile)), cfg)
    result.triggered shouldBe true
    result.points shouldBe 25
  }

  "a watch-listed category over its amount threshold" should "trigger RiskyCategory" in {
    val result = RiskRules.riskyCategory(ctx(tx(amount = BigDecimal(3500), category = "Electronics")), cfg)
    result.triggered shouldBe true
    result.points shouldBe 15
  }

  "a watch-listed category under its amount threshold" should "not trigger RiskyCategory" in {
    val result = RiskRules.riskyCategory(ctx(tx(amount = BigDecimal(50), category = "Electronics")), cfg)
    result.triggered shouldBe false
  }

  // ---- Combined scoring ----

  "a very large amount from a risky country" should "sum points from both rules and be flagged" in {
    val assessment = RiskRules.evaluate(ctx(tx(amount = BigDecimal(15000), country = "RU")), cfg)
    assessment.riskScore shouldBe 65 // 45 (very large) + 20 (risky country)
    assessment.flagged shouldBe true
    assessment.triggeredRules should contain allOf ("VeryLargeAmount", "RiskyCountry")
  }

  "risk score" should "cap at 100 even if triggered points exceed it" in {
    val profile = CustomerProfile("acct-1", transactionCount = 10, avgAmount = BigDecimal(50),
      stdDevAmount = BigDecimal(10), maxAmount = BigDecimal(100), avgTransactionsPerDay = 1.0)
    val assessment = RiskRules.evaluate(
      ctx(tx(amount = BigDecimal(20000), country = "KP", category = "Electronics"),
        recentCount = 5, todayCount = 10, profile = Some(profile)),
      cfg
    )
    assessment.riskScore should be <= 100
  }
}
