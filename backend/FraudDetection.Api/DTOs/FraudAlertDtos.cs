using FraudDetection.Api.Models;

namespace FraudDetection.Api.DTOs;

public class FraudAlertDto
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public decimal RiskScore { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public decimal TransactionAmount { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? TriggeredRules { get; set; }

    public static FraudAlertDto FromEntity(FraudAlert a) => new()
    {
        Id = a.Id,
        TransactionId = a.TransactionId,
        RiskScore = a.RiskScore,
        Severity = a.Severity,
        Status = a.Status,
        Reason = a.Reason,
        CreatedAtUtc = a.CreatedAtUtc,
        MerchantName = a.Transaction?.MerchantName ?? string.Empty,
        TransactionAmount = a.Transaction?.Amount ?? 0,
        Source = a.Source,
        TriggeredRules = a.TriggeredRules
    };
}

public class TopTriggerDto
{
    public string RuleName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DashboardStatsDto
{
    public int TotalTransactions { get; set; }
    public int TotalAlerts { get; set; }
    public int OpenAlerts { get; set; }
    public double FraudRate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal FlaggedAmount { get; set; }
    public Dictionary<string, int> AlertsBySeverity { get; set; } = new();
}

public class DailyTrendDto
{
    public DateTime Date { get; set; }
    public int TransactionCount { get; set; }
    public int FlaggedCount { get; set; }
    public decimal TotalAmount { get; set; }
}
