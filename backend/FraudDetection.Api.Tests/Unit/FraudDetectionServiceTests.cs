using FraudDetection.Api.Data;
using FraudDetection.Api.Models;
using FraudDetection.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FraudDetection.Api.Tests.Unit;

/// <summary>
/// Tests for the Phase 1/2 C# rule engine. No longer wired into the live pipeline (Scala
/// owns real-time scoring as of Phase 3) but the logic is unchanged and still worth
/// covering -- it's the reference implementation the Scala rules were ported from.
/// </summary>
public class FraudDetectionServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Transaction NewTransaction(decimal amount = 50, string country = "US") => new()
    {
        AccountId = Guid.NewGuid(),
        MerchantName = "Test Merchant",
        MerchantCategory = "Retail",
        Amount = amount,
        Currency = "USD",
        CountryCode = country,
        OccurredAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task EvaluateAsync_ReturnsNull_ForOrdinaryTransaction()
    {
        await using var db = NewDb();
        var service = new FraudDetectionService(db);

        var result = await service.EvaluateAsync(NewTransaction());

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_FlagsVeryLargeAmount()
    {
        await using var db = NewDb();
        var service = new FraudDetectionService(db);
        var transaction = NewTransaction(amount: 15_000);

        var alert = await service.EvaluateAsync(transaction);

        Assert.NotNull(alert);
        Assert.Equal(TransactionStatus.Flagged, transaction.Status);
        Assert.True(alert!.RiskScore >= 45);
    }

    [Fact]
    public async Task EvaluateAsync_FlagsRiskyCountryCombinedWithLargeAmount()
    {
        await using var db = NewDb();
        var service = new FraudDetectionService(db);
        var transaction = NewTransaction(amount: 12_000, country: "RU");

        var alert = await service.EvaluateAsync(transaction);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity); // 45 + 20 = 65
        Assert.Contains("high-risk country", alert.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotFlag_RiskyCountryAloneBelowThreshold()
    {
        // Risky country alone is 20 points, below the implicit "Low severity -> no alert" cutoff.
        await using var db = NewDb();
        var service = new FraudDetectionService(db);
        var transaction = NewTransaction(amount: 50, country: "RU");

        var alert = await service.EvaluateAsync(transaction);

        Assert.Null(alert);
    }

    [Fact]
    public async Task EvaluateAsync_FlagsVelocity_WhenCombinedWithAnotherRuleCrossesAlertThreshold()
    {
        // Velocity alone is 35 points -- below the >=40 "Medium" cutoff EvaluateAsync uses to
        // decide whether Low-severity hits are even worth an alert, so it's paired here with a
        // risky-country hit (20 points) to reach 55 and actually produce an alert.
        await using var db = NewDb();
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Transactions.AddRange(
            new Transaction { AccountId = accountId, MerchantName = "A", MerchantCategory = "Retail", Amount = 10, CountryCode = "RU", OccurredAtUtc = now.AddMinutes(-2) },
            new Transaction { AccountId = accountId, MerchantName = "B", MerchantCategory = "Retail", Amount = 10, CountryCode = "RU", OccurredAtUtc = now.AddMinutes(-4) },
            new Transaction { AccountId = accountId, MerchantName = "C", MerchantCategory = "Retail", Amount = 10, CountryCode = "RU", OccurredAtUtc = now.AddMinutes(-6) }
        );
        await db.SaveChangesAsync();

        var service = new FraudDetectionService(db);
        var newTransaction = new Transaction
        {
            AccountId = accountId,
            MerchantName = "D",
            MerchantCategory = "Retail",
            Amount = 10,
            CountryCode = "RU",
            OccurredAtUtc = now
        };

        var alert = await service.EvaluateAsync(newTransaction);

        Assert.NotNull(alert);
        Assert.Contains("transactions from the same account within 10 minutes", alert!.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DetectsVelocity_ButDoesNotAlert_WhenScoreStaysBelowThreshold()
    {
        // Documents the real (slightly surprising) behavior: HighVelocity alone never alerts,
        // because 35 points doesn't cross the >=40 cutoff. This is the Phase 1 baseline the
        // Scala engine's velocity rule (configurable weight, see RiskRulesConfig) improves on.
        await using var db = NewDb();
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Transactions.AddRange(
            new Transaction { AccountId = accountId, MerchantName = "A", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-2) },
            new Transaction { AccountId = accountId, MerchantName = "B", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-4) },
            new Transaction { AccountId = accountId, MerchantName = "C", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-6) }
        );
        await db.SaveChangesAsync();

        var service = new FraudDetectionService(db);
        var newTransaction = new Transaction
        {
            AccountId = accountId,
            MerchantName = "D",
            MerchantCategory = "Retail",
            Amount = 10,
            CountryCode = "US",
            OccurredAtUtc = now
        };

        var alert = await service.EvaluateAsync(newTransaction);

        Assert.Null(alert);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresTransactionsOutsideVelocityWindow()
    {
        await using var db = NewDb();
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Transactions.AddRange(
            new Transaction { AccountId = accountId, MerchantName = "A", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-20) },
            new Transaction { AccountId = accountId, MerchantName = "B", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-30) },
            new Transaction { AccountId = accountId, MerchantName = "C", MerchantCategory = "Retail", Amount = 10, CountryCode = "US", OccurredAtUtc = now.AddMinutes(-40) }
        );
        await db.SaveChangesAsync();

        var service = new FraudDetectionService(db);
        var newTransaction = new Transaction
        {
            AccountId = accountId,
            MerchantName = "D",
            MerchantCategory = "Retail",
            Amount = 10,
            CountryCode = "US",
            OccurredAtUtc = now
        };

        var alert = await service.EvaluateAsync(newTransaction);

        Assert.Null(alert);
    }
}
