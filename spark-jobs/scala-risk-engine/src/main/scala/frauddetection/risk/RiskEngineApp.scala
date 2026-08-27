package frauddetection.risk

import org.apache.logging.log4j.Level
import org.apache.logging.log4j.core.config.Configurator
import org.apache.spark.sql.{DataFrame, Row, SparkSession}
import org.apache.spark.sql.functions._
import org.apache.spark.sql.streaming.Trigger
import org.apache.spark.sql.types._
import org.slf4j.LoggerFactory

/**
 * Structured Streaming entrypoint. Reads transactions.created from Kafka, and for every
 * micro-batch, scores each transaction against RiskRules (pulling velocity/frequency counts
 * and the PySpark-computed customer profile from Postgres) and writes the verdict straight
 * back to Postgres via JDBC -- see PostgresJdbc.writeResult.
 */
object RiskEngineApp {
  private val logger = LoggerFactory.getLogger("frauddetection.risk.RiskEngineApp")

  // Mirrors FraudDetection.Api.Messaging.EventEnvelope<TransactionCreatedEventV1> (PascalCase --
  // this is the exact wire format System.Text.Json.JsonSerializer.Serialize produces by default).
  private val payloadSchema = StructType(Seq(
    StructField("TransactionId", StringType),
    StructField("AccountId", StringType),
    StructField("MerchantName", StringType),
    StructField("MerchantCategory", StringType),
    StructField("Amount", DecimalType(18, 2)),
    StructField("Currency", StringType),
    StructField("CountryCode", StringType),
    StructField("OccurredAtUtc", StringType)
  ))

  private val envelopeSchema = StructType(Seq(
    StructField("EventId", StringType),
    StructField("EventType", StringType),
    StructField("EventVersion", IntegerType),
    StructField("OccurredAtUtc", StringType),
    StructField("Payload", payloadSchema)
  ))

  def main(args: Array[String]): Unit = {
    val config = AppConfig.load()
    Class.forName("org.postgresql.Driver")

    val spark = SparkSession.builder().appName("scala-risk-engine").getOrCreate()
    spark.sparkContext.setLogLevel("WARN")
    // SparkContext.setLogLevel resets the root logger, which silently swallows our own
    // structured logs regardless of the log4j2.properties bundled in the app jar (classpath
    // resolution between that and Spark's own logging config is unreliable inside
    // spark-submit). Force our package back to INFO explicitly, after Spark's own call.
    Configurator.setLevel("frauddetection.risk", Level.INFO)

    logger.info(s"event=startup kafka=${config.kafka.bootstrapServers} topic=${config.kafka.topic} " +
      s"flagThreshold=${config.rules.flagThreshold} triggerIntervalSeconds=${config.processing.triggerIntervalSeconds}")

    val raw = spark.readStream
      .format("kafka")
      .option("kafka.bootstrap.servers", config.kafka.bootstrapServers)
      .option("subscribe", config.kafka.topic)
      .option("startingOffsets", "earliest")
      .option("failOnDataLoss", "false")
      .load()

    val parsed = raw
      .selectExpr("CAST(value AS STRING) AS json")
      .select(from_json(col("json"), envelopeSchema).as("envelope"))
      .select("envelope.Payload.*")

    val query = parsed.writeStream
      .foreachBatch { (batchDf: DataFrame, batchId: Long) => processBatch(batchDf, batchId, config) }
      .trigger(Trigger.ProcessingTime(s"${config.processing.triggerIntervalSeconds} seconds"))
      .option("checkpointLocation", config.processing.checkpointLocation)
      .start()

    query.awaitTermination()
  }

  private def processBatch(batchDf: DataFrame, batchId: Long, config: AppConfig): Unit = {
    val size = batchDf.count()
    logger.info(s"event=batch_seen batchId=$batchId size=$size")
    if (size == 0L) return

    val batchStart = System.currentTimeMillis()
    logger.info(s"event=batch_start batchId=$batchId size=$size")

    // One JDBC connection per partition -- distributed across executors, not collected to the driver.
    batchDf.foreachPartition { partition: Iterator[Row] =>
      val log = LoggerFactory.getLogger("frauddetection.risk.RiskEngineApp")
      Class.forName("org.postgresql.Driver")
      val conn = java.sql.DriverManager.getConnection(config.postgres.url, config.postgres.user, config.postgres.password)
      try {
        partition.foreach { row =>
          val txStart = System.currentTimeMillis()
          try {
            val tx = RowMapper.toTransactionEvent(row)
            val recentCount = PostgresJdbc.queryRecentCount(conn, tx, config.rules.velocityWindowSeconds)
            val todayCount = PostgresJdbc.queryTodayCount(conn, tx)
            val profile = PostgresJdbc.queryProfile(conn, tx.accountId)
            val assessment = RiskRules.evaluate(RuleContext(tx, recentCount, todayCount, profile), config.rules)
            PostgresJdbc.writeResult(conn, tx, assessment)

            val latencyMs = System.currentTimeMillis() - txStart
            log.info(s"event=risk_scored transactionId=${tx.transactionId} accountId=${tx.accountId} " +
              s"amount=${tx.amount} riskScore=${assessment.riskScore} flagged=${assessment.flagged} " +
              s"""rules=[${assessment.triggeredRules.mkString(",")}] latencyMs=$latencyMs""")
          } catch {
            case e: Exception =>
              log.error(s"event=transaction_processing_error batchId=$batchId error=${e.getMessage}", e)
          }
        }
      } finally {
        conn.close()
      }
    }

    val elapsedMs = System.currentTimeMillis() - batchStart
    logger.info(s"event=batch_complete batchId=$batchId size=$size elapsedMs=$elapsedMs " +
      f"avgMsPerTransaction=${elapsedMs.toDouble / size}%.1f")
  }
}
