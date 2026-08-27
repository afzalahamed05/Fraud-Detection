using FraudDetection.Api.Models;

namespace FraudDetection.Api.DTOs;

public class CustomerRiskProfileDto
{
    public Guid AccountId { get; set; }
    public int TransactionCount { get; set; }
    public decimal AvgAmount { get; set; }
    public decimal StdDevAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public int DistinctMerchantCategories { get; set; }
    public int DistinctCountries { get; set; }
    public decimal AvgTransactionsPerDay { get; set; }
    public DateTime? LastTransactionAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public static CustomerRiskProfileDto FromEntity(CustomerRiskProfile p) => new()
    {
        AccountId = p.AccountId,
        TransactionCount = p.TransactionCount,
        AvgAmount = p.AvgAmount,
        StdDevAmount = p.StdDevAmount,
        MaxAmount = p.MaxAmount,
        DistinctMerchantCategories = p.DistinctMerchantCategories,
        DistinctCountries = p.DistinctCountries,
        AvgTransactionsPerDay = p.AvgTransactionsPerDay,
        LastTransactionAtUtc = p.LastTransactionAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc
    };
}
