using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FraudDetection.Api.Models;

public class FraudAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal RiskScore { get; set; }

    public AlertSeverity Severity { get; set; }

    public AlertStatus Status { get; set; } = AlertStatus.Open;

    [Required, MaxLength(250)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Which engine raised this alert: "ScalaRiskEngine" (per-transaction rules) or
    /// "PySparkAnomalyDetection" (statistical deviation from the customer's historical baseline).</summary>
    [Required, MaxLength(30)]
    public string Source { get; set; } = "ScalaRiskEngine";

    /// <summary>JSON array of rule names that fired, e.g. ["LargeAmount","HighVelocity"].</summary>
    [MaxLength(500)]
    public string? TriggeredRules { get; set; }
}
