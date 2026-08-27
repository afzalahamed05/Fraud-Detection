package frauddetection.risk

/**
 * Pure, Spark-free rule engine. Every rule is a plain function over (RuleContext, config) so
 * it can be unit tested with plain case classes -- no SparkSession required. RiskEngineApp
 * is the thin glue that gets a RuleContext built from Kafka + Postgres and calls evaluate().
 */
object RiskRules {

  def largeAmount(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = {
    val amount = ctx.transaction.amount
    if (amount > cfg.veryLargeAmountThreshold) {
      RuleResult("VeryLargeAmount", triggered = true, cfg.weights.veryLargeAmount,
        s"Amount $$${amount} exceeds very-large threshold $$${cfg.veryLargeAmountThreshold}")
    } else if (amount > cfg.largeAmountThreshold) {
      RuleResult("LargeAmount", triggered = true, cfg.weights.largeAmount,
        s"Amount $$${amount} exceeds large threshold $$${cfg.largeAmountThreshold}")
    } else {
      RuleResult("LargeAmount", triggered = false, 0, "")
    }
  }

  def riskyCountry(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = {
    val country = ctx.transaction.countryCode
    if (cfg.riskyCountries.contains(country)) {
      RuleResult("RiskyCountry", triggered = true, cfg.weights.riskyCountry,
        s"Country $country is on the high-risk list")
    } else {
      RuleResult("RiskyCountry", triggered = false, 0, "")
    }
  }

  /** "multiple transactions within a short time window" */
  def velocity(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = {
    if (ctx.recentTransactionCountInWindow >= cfg.velocityCountThreshold) {
      RuleResult("HighVelocity", triggered = true, cfg.weights.velocity,
        s"${ctx.recentTransactionCountInWindow} transactions from this account in the last " +
          s"${cfg.velocityWindowSeconds}s (threshold ${cfg.velocityCountThreshold})")
    } else {
      RuleResult("HighVelocity", triggered = false, 0, "")
    }
  }

  /** "rapid spending escalation": this transaction dwarfs the customer's historical average. */
  def escalation(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = ctx.profile match {
    case Some(p) if p.transactionCount >= 3 && p.avgAmount > 0 &&
        ctx.transaction.amount > p.avgAmount * cfg.escalationMultiplier =>
      RuleResult("SpendingEscalation", triggered = true, cfg.weights.escalation,
        s"Amount $$${ctx.transaction.amount} is ${cfg.escalationMultiplier}x+ this account's " +
          s"historical average ($$${p.avgAmount.setScale(2, BigDecimal.RoundingMode.HALF_UP)})")
    case _ =>
      RuleResult("SpendingEscalation", triggered = false, 0, "")
  }

  /** "unusual transaction frequency": today's activity far exceeds this customer's daily norm. */
  def frequency(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = ctx.profile match {
    case Some(p) if p.avgTransactionsPerDay > 0 &&
        ctx.todayTransactionCount > p.avgTransactionsPerDay * cfg.frequencyMultiplier =>
      RuleResult("UnusualFrequency", triggered = true, cfg.weights.frequency,
        s"${ctx.todayTransactionCount} transactions today vs a daily average of " +
          f"${p.avgTransactionsPerDay}%.1f (${cfg.frequencyMultiplier}x threshold)")
    case _ =>
      RuleResult("UnusualFrequency", triggered = false, 0, "")
  }

  /** "suspicious merchant/category patterns": watch-listed category combined with a real amount. */
  def riskyCategory(ctx: RuleContext, cfg: RiskRulesConfig): RuleResult = {
    val category = ctx.transaction.merchantCategory
    if (cfg.riskyCategories.contains(category) && ctx.transaction.amount > cfg.riskyCategoryAmountThreshold) {
      RuleResult("RiskyCategory", triggered = true, cfg.weights.riskyCategory,
        s"$$${ctx.transaction.amount} in watch-listed category '$category'")
    } else {
      RuleResult("RiskyCategory", triggered = false, 0, "")
    }
  }

  private val allRules: Seq[(RuleContext, RiskRulesConfig) => RuleResult] =
    Seq(largeAmount, riskyCountry, velocity, escalation, frequency, riskyCategory)

  def evaluate(ctx: RuleContext, cfg: RiskRulesConfig): RiskAssessment = {
    val results = allRules.map(rule => rule(ctx, cfg))
    val triggered = results.filter(_.triggered)
    val score = math.min(100, triggered.map(_.points).sum)

    RiskAssessment(
      transactionId = ctx.transaction.transactionId,
      riskScore = score,
      flagged = score >= cfg.flagThreshold,
      triggeredRules = triggered.map(_.ruleName).toList,
      reasons = triggered.map(_.reason).toList
    )
  }
}
