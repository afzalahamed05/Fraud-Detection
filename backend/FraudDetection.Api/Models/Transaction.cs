using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FraudDetection.Api.Models;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AccountId { get; set; }

    [Required, MaxLength(120)]
    public string MerchantName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string MerchantCategory { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [MaxLength(2)]
    public string CountryCode { get; set; } = "US";

    public TransactionStatus Status { get; set; } = TransactionStatus.Approved;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set once the creating request successfully hands the event to Kafka. Null means the
    /// outbox sweep (<see cref="Services.TransactionOutboxService"/>) still needs to (re)publish it.</summary>
    public DateTime? PublishedToKafkaUtc { get; set; }

    /// <summary>Set by the consumer once fraud scoring for this transaction has completed.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    [MaxLength(500)]
    public string? ProcessingError { get; set; }

    public int ProcessingAttempts { get; set; }

    /// <summary>Deterministic risk score (0-100) computed by the Scala Structured Streaming
    /// risk engine for every transaction, whether or not it crossed the alert threshold.</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal? RiskScore { get; set; }

    /// <summary>Which pipeline last scored this transaction, e.g. "ScalaRiskEngine".</summary>
    [MaxLength(30)]
    public string? ProcessingSource { get; set; }

    public ICollection<FraudAlert> FraudAlerts { get; set; } = new List<FraudAlert>();
}
