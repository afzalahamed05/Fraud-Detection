using FraudDetection.Api.Data;
using FraudDetection.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Services;

/// <summary>
/// Deterministic rule-based risk scorer. Kafka/Spark streaming will replace/augment
/// this in a later phase; the scoring rules stay the same so behavior is comparable.
/// </summary>
public class FraudDetectionService
{
    private static readonly HashSet<string> TrustedCountries = new() { "US", "CA", "GB", "DE", "FR", "AU" };
    private readonly AppDbContext _db;

    public FraudDetectionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FraudAlert?> EvaluateAsync(Transaction transaction, CancellationToken ct = default)
    {
        var score = 0m;
        var reasons = new List<string>();

        if (transaction.Amount > 10_000)
        {
            score += 45;
            reasons.Add("Very high transaction amount");
        }
        else if (transaction.Amount > 5_000)
        {
            score += 25;
            reasons.Add("High transaction amount");
        }

        if (!TrustedCountries.Contains(transaction.CountryCode))
        {
            score += 20;
            reasons.Add("Transaction from high-risk country");
        }

        var windowStart = transaction.OccurredAtUtc.AddMinutes(-10);
        var recentCount = await _db.Transactions
            .Where(t => t.AccountId == transaction.AccountId
                        && t.Id != transaction.Id
                        && t.OccurredAtUtc >= windowStart
                        && t.OccurredAtUtc <= transaction.OccurredAtUtc)
            .CountAsync(ct);

        if (recentCount >= 3)
        {
            score += 35;
            reasons.Add($"{recentCount} transactions from the same account within 10 minutes");
        }

        if (score <= 0)
        {
            return null;
        }

        var severity = score switch
        {
            >= 80 => AlertSeverity.Critical,
            >= 60 => AlertSeverity.High,
            >= 40 => AlertSeverity.Medium,
            _ => AlertSeverity.Low
        };

        if (severity is AlertSeverity.Low)
        {
            return null;
        }

        transaction.Status = TransactionStatus.Flagged;

        return new FraudAlert
        {
            TransactionId = transaction.Id,
            RiskScore = Math.Min(score, 100),
            Severity = severity,
            Reason = string.Join("; ", reasons)
        };
    }
}
