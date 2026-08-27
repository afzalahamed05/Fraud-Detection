using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FraudDetection.Api.Models;

/// <summary>
/// Behavioral baseline for one account, recomputed periodically by the PySpark analytics
/// job from transaction history. The Scala risk engine reads this to judge whether a new
/// transaction is unusual *for this specific customer* (not just against a global threshold).
/// </summary>
public class CustomerRiskProfile
{
    [Key]
    public Guid AccountId { get; set; }

    public int TransactionCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AvgAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StdDevAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MaxAmount { get; set; }

    public int DistinctMerchantCategories { get; set; }

    public int DistinctCountries { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal AvgTransactionsPerDay { get; set; }

    public DateTime? LastTransactionAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
