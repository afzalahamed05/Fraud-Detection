package frauddetection.risk

import java.sql.{Connection, Timestamp}
import java.time.Instant

/** Raw JDBC (no ORM) against the same schema FraudDetection.Api's EF Core migrations own.
 * Runs on Spark executors inside foreachPartition, one connection per partition. */
object PostgresJdbc {

  def queryRecentCount(conn: Connection, tx: TransactionEvent, windowSeconds: Int): Int = {
    val windowStart = Timestamp.from(tx.occurredAtUtc.minusSeconds(windowSeconds))
    val stmt = conn.prepareStatement(
      """SELECT COUNT(*) FROM transactions
        |WHERE "AccountId" = ?::uuid AND "Id" != ?::uuid
        |  AND "OccurredAtUtc" >= ? AND "OccurredAtUtc" <= ?""".stripMargin
    )
    try {
      stmt.setString(1, tx.accountId)
      stmt.setString(2, tx.transactionId)
      stmt.setTimestamp(3, windowStart)
      stmt.setTimestamp(4, Timestamp.from(tx.occurredAtUtc))
      val rs = stmt.executeQuery()
      if (rs.next()) rs.getInt(1) else 0
    } finally stmt.close()
  }

  def queryTodayCount(conn: Connection, tx: TransactionEvent): Int = {
    val stmt = conn.prepareStatement(
      """SELECT COUNT(*) FROM transactions
        |WHERE "AccountId" = ?::uuid AND date_trunc('day', "OccurredAtUtc") = date_trunc('day', ?::timestamptz)"""
        .stripMargin
    )
    try {
      stmt.setString(1, tx.accountId)
      stmt.setTimestamp(2, Timestamp.from(tx.occurredAtUtc))
      val rs = stmt.executeQuery()
      if (rs.next()) rs.getInt(1) else 0
    } finally stmt.close()
  }

  def queryProfile(conn: Connection, accountId: String): Option[CustomerProfile] = {
    val stmt = conn.prepareStatement(
      """SELECT "TransactionCount", "AvgAmount", "StdDevAmount", "MaxAmount", "AvgTransactionsPerDay"
        |FROM customer_risk_profiles WHERE "AccountId" = ?::uuid""".stripMargin
    )
    try {
      stmt.setString(1, accountId)
      val rs = stmt.executeQuery()
      if (rs.next()) {
        Some(CustomerProfile(
          accountId = accountId,
          transactionCount = rs.getInt("TransactionCount"),
          avgAmount = BigDecimal(rs.getBigDecimal("AvgAmount")),
          stdDevAmount = BigDecimal(rs.getBigDecimal("StdDevAmount")),
          maxAmount = BigDecimal(rs.getBigDecimal("MaxAmount")),
          avgTransactionsPerDay = rs.getDouble("AvgTransactionsPerDay")
        ))
      } else None
    } finally stmt.close()
  }

  private def severityFor(riskScore: Int): String =
    if (riskScore >= 80) "Critical" else if (riskScore >= 60) "High" else "Medium"

  /** Idempotent: only updates rows still Pending, mirroring the guard the old C# consumer used
   * (redelivery of an already-processed message is a safe no-op). */
  def writeResult(conn: Connection, tx: TransactionEvent, assessment: RiskAssessment): Boolean = {
    val status = if (assessment.flagged) "Flagged" else "Approved"
    val updateStmt = conn.prepareStatement(
      """UPDATE transactions
        |SET "Status" = ?, "RiskScore" = ?, "ProcessingSource" = 'ScalaRiskEngine',
        |    "ProcessedAtUtc" = now(), "ProcessingError" = NULL
        |WHERE "Id" = ?::uuid AND "Status" = 'Pending'""".stripMargin
    )
    val updated =
      try {
        updateStmt.setString(1, status)
        updateStmt.setBigDecimal(2, BigDecimal(assessment.riskScore).bigDecimal.setScale(2))
        updateStmt.setString(3, tx.transactionId)
        updateStmt.executeUpdate() > 0
      } finally updateStmt.close()

    if (updated && assessment.flagged) {
      val rulesJson = assessment.triggeredRules.map(r => s""""$r"""").mkString("[", ",", "]")
      val reason = assessment.reasons.mkString("; ")
      val insertStmt = conn.prepareStatement(
        """INSERT INTO fraud_alerts ("Id", "TransactionId", "RiskScore", "Severity", "Status",
          |  "Reason", "CreatedAtUtc", "Source", "TriggeredRules")
          |VALUES (gen_random_uuid(), ?::uuid, ?, ?, 'Open', ?, now(), 'ScalaRiskEngine', ?)""".stripMargin
      )
      try {
        insertStmt.setString(1, tx.transactionId)
        insertStmt.setBigDecimal(2, BigDecimal(assessment.riskScore).bigDecimal.setScale(2))
        insertStmt.setString(3, severityFor(assessment.riskScore))
        insertStmt.setString(4, if (reason.isEmpty) "Flagged by Scala risk engine" else reason)
        insertStmt.setString(5, rulesJson)
        insertStmt.executeUpdate()
      } finally insertStmt.close()
    }

    updated
  }
}
