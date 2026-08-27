using System.ComponentModel.DataAnnotations;
using FraudDetection.Api.Models;

namespace FraudDetection.Api.DTOs;

public class CreateTransactionDto
{
    [Required]
    public Guid AccountId { get; set; }

    [Required, MaxLength(120)]
    public string MerchantName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string MerchantCategory { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [MaxLength(2)]
    public string CountryCode { get; set; } = "US";

    public DateTime? OccurredAtUtc { get; set; }
}

public class TransactionDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantCategory { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public TransactionStatus Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int AlertCount { get; set; }
    public DateTime? PublishedToKafkaUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ProcessingError { get; set; }
    public decimal? RiskScore { get; set; }
    public string? ProcessingSource { get; set; }

    public static TransactionDto FromEntity(Transaction t) => new()
    {
        Id = t.Id,
        AccountId = t.AccountId,
        MerchantName = t.MerchantName,
        MerchantCategory = t.MerchantCategory,
        Amount = t.Amount,
        Currency = t.Currency,
        CountryCode = t.CountryCode,
        Status = t.Status,
        OccurredAtUtc = t.OccurredAtUtc,
        AlertCount = t.FraudAlerts?.Count ?? 0,
        PublishedToKafkaUtc = t.PublishedToKafkaUtc,
        ProcessedAtUtc = t.ProcessedAtUtc,
        ProcessingError = t.ProcessingError,
        RiskScore = t.RiskScore,
        ProcessingSource = t.ProcessingSource
    };
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
