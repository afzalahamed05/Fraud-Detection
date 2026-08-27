package frauddetection.risk

import com.typesafe.config.{Config, ConfigFactory}
import scala.collection.JavaConverters._

case class RuleWeights(
    largeAmount: Int,
    veryLargeAmount: Int,
    riskyCountry: Int,
    velocity: Int,
    escalation: Int,
    frequency: Int,
    riskyCategory: Int
)

case class RiskRulesConfig(
    largeAmountThreshold: BigDecimal,
    veryLargeAmountThreshold: BigDecimal,
    velocityWindowSeconds: Int,
    velocityCountThreshold: Int,
    escalationMultiplier: Double,
    frequencyMultiplier: Double,
    riskyCountries: Set[String],
    riskyCategories: Set[String],
    riskyCategoryAmountThreshold: BigDecimal,
    flagThreshold: Int,
    weights: RuleWeights
)

case class KafkaConfig(bootstrapServers: String, topic: String)
case class PostgresConfig(url: String, user: String, password: String)
case class ProcessingConfig(triggerIntervalSeconds: Int, checkpointLocation: String)

case class AppConfig(
    kafka: KafkaConfig,
    postgres: PostgresConfig,
    rules: RiskRulesConfig,
    processing: ProcessingConfig
)

object AppConfig {
  def load(): AppConfig = fromTypesafeConfig(ConfigFactory.load().getConfig("risk-engine"))

  def fromTypesafeConfig(c: Config): AppConfig = {
    val rulesConf = c.getConfig("rules")
    val weightsConf = rulesConf.getConfig("weights")

    AppConfig(
      kafka = KafkaConfig(
        bootstrapServers = c.getString("kafka.bootstrap-servers"),
        topic = c.getString("kafka.topic")
      ),
      postgres = PostgresConfig(
        url = c.getString("postgres.url"),
        user = c.getString("postgres.user"),
        password = c.getString("postgres.password")
      ),
      rules = RiskRulesConfig(
        largeAmountThreshold = BigDecimal(rulesConf.getDouble("large-amount-threshold")),
        veryLargeAmountThreshold = BigDecimal(rulesConf.getDouble("very-large-amount-threshold")),
        velocityWindowSeconds = rulesConf.getInt("velocity-window-seconds"),
        velocityCountThreshold = rulesConf.getInt("velocity-count-threshold"),
        escalationMultiplier = rulesConf.getDouble("escalation-multiplier"),
        frequencyMultiplier = rulesConf.getDouble("frequency-multiplier"),
        riskyCountries = rulesConf.getStringList("risky-countries").asScala.toSet,
        riskyCategories = rulesConf.getStringList("risky-categories").asScala.toSet,
        riskyCategoryAmountThreshold = BigDecimal(rulesConf.getDouble("risky-category-amount-threshold")),
        flagThreshold = rulesConf.getInt("flag-threshold"),
        weights = RuleWeights(
          largeAmount = weightsConf.getInt("large-amount"),
          veryLargeAmount = weightsConf.getInt("very-large-amount"),
          riskyCountry = weightsConf.getInt("risky-country"),
          velocity = weightsConf.getInt("velocity"),
          escalation = weightsConf.getInt("escalation"),
          frequency = weightsConf.getInt("frequency"),
          riskyCategory = weightsConf.getInt("risky-category")
        )
      ),
      processing = ProcessingConfig(
        triggerIntervalSeconds = c.getInt("processing.trigger-interval-seconds"),
        checkpointLocation = c.getString("processing.checkpoint-location")
      )
    )
  }
}
