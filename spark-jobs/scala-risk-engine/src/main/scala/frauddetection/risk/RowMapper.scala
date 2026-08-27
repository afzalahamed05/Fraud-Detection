package frauddetection.risk

import org.apache.spark.sql.Row
import java.time.Instant

object RowMapper {
  def toTransactionEvent(row: Row): TransactionEvent = TransactionEvent(
    transactionId = row.getAs[String]("TransactionId"),
    accountId = row.getAs[String]("AccountId"),
    merchantName = row.getAs[String]("MerchantName"),
    merchantCategory = row.getAs[String]("MerchantCategory"),
    amount = BigDecimal(row.getAs[java.math.BigDecimal]("Amount")),
    currency = row.getAs[String]("Currency"),
    countryCode = row.getAs[String]("CountryCode"),
    occurredAtUtc = Instant.parse(row.getAs[String]("OccurredAtUtc"))
  )
}
