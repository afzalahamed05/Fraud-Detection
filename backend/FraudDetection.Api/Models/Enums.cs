namespace FraudDetection.Api.Models;

public enum TransactionStatus
{
    Pending,
    Approved,
    Declined,
    Flagged
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlertStatus
{
    Open,
    Reviewed,
    Dismissed
}
