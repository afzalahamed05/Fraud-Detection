using FraudDetection.Api.Models;
using FraudDetection.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Data;

public static class SeedData
{
    private static readonly string[] Merchants =
    {
        "Amazon", "Walmart", "Target", "Best Buy", "Steam", "Uber", "Delta Airlines",
        "Shell Gas", "Starbucks", "Apple Store", "Netflix", "AirBnB", "DoorDash",
        "Home Depot", "CVS Pharmacy", "Costco", "eBay", "PlayStation Store"
    };

    private static readonly string[] Categories =
    {
        "Retail", "Travel", "Groceries", "Electronics", "Entertainment",
        "Dining", "Fuel", "Healthcare", "Subscription", "Gaming"
    };

    private static readonly string[] TrustedCountries = { "US", "CA", "GB", "DE", "FR", "AU" };
    private static readonly string[] RiskyCountries = { "RU", "NG", "KP", "IR", "BR" };

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Transactions.AnyAsync())
        {
            return;
        }

        var random = new Random(42);
        var accountIds = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        var now = DateTime.UtcNow;

        var transactions = new List<Transaction>();

        for (var i = 0; i < 500; i++)
        {
            var accountId = accountIds[random.Next(accountIds.Length)];
            var isRisky = random.NextDouble() < 0.08;
            var isHighAmount = random.NextDouble() < 0.1;

            var amount = isHighAmount
                ? Math.Round((decimal)(random.NextDouble() * 15000 + 5000), 2)
                : Math.Round((decimal)(random.NextDouble() * 480 + 5), 2);

            var country = isRisky
                ? RiskyCountries[random.Next(RiskyCountries.Length)]
                : TrustedCountries[random.Next(TrustedCountries.Length)];

            var occurredAt = now.AddMinutes(-random.Next(0, 60 * 24 * 14));

            transactions.Add(new Transaction
            {
                AccountId = accountId,
                MerchantName = Merchants[random.Next(Merchants.Length)],
                MerchantCategory = Categories[random.Next(Categories.Length)],
                Amount = amount,
                Currency = "USD",
                CountryCode = country,
                OccurredAtUtc = occurredAt,
                CreatedAtUtc = occurredAt
            });
        }

        // A handful of deliberate "velocity burst" accounts: several transactions
        // within a few minutes of each other, to exercise that fraud rule.
        for (var b = 0; b < 6; b++)
        {
            var burstAccount = Guid.NewGuid();
            var burstStart = now.AddMinutes(-random.Next(0, 60 * 24 * 10));
            for (var j = 0; j < random.Next(4, 7); j++)
            {
                var occurredAt = burstStart.AddMinutes(j * 1.5);
                transactions.Add(new Transaction
                {
                    AccountId = burstAccount,
                    MerchantName = Merchants[random.Next(Merchants.Length)],
                    MerchantCategory = Categories[random.Next(Categories.Length)],
                    Amount = Math.Round((decimal)(random.NextDouble() * 200 + 20), 2),
                    Currency = "USD",
                    CountryCode = "US",
                    OccurredAtUtc = occurredAt,
                    CreatedAtUtc = occurredAt
                });
            }
        }

        // Evaluate fraud rules in chronological order so the velocity check sees
        // prior transactions the same way it would in real usage.
        transactions = transactions.OrderBy(t => t.OccurredAtUtc).ToList();
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();

        var fraudDetection = new FraudDetectionService(db);
        var alerts = new List<FraudAlert>();
        foreach (var transaction in transactions)
        {
            var alert = await fraudDetection.EvaluateAsync(transaction);
            if (alert is not null)
            {
                alerts.Add(alert);
            }
        }

        db.FraudAlerts.AddRange(alerts);
        await db.SaveChangesAsync();
    }
}
