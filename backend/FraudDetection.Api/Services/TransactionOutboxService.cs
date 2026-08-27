using FraudDetection.Api.Data;
using FraudDetection.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Services;

/// <summary>
/// Safety net for the "failures do not silently lose transactions" requirement: the API
/// tries to publish to Kafka inline when a transaction is created, but if that fails
/// (broker unreachable, timeout, etc.) the row is still durably in Postgres with
/// PublishedToKafkaUtc == null. This sweep periodically finds those and retries.
/// </summary>
public class TransactionOutboxService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TransactionOutboxService> _logger;

    public TransactionOutboxService(IServiceScopeFactory scopeFactory, ILogger<TransactionOutboxService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox sweep failed");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<KafkaProducerService>();

        var unpublished = await db.Transactions
            .Where(t => t.PublishedToKafkaUtc == null)
            .OrderBy(t => t.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        if (unpublished.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Outbox sweep: retrying {Count} unpublished transaction(s)", unpublished.Count);

        foreach (var transaction in unpublished)
        {
            var published = await producer.PublishTransactionCreatedAsync(new Messaging.TransactionCreatedEventV1
            {
                TransactionId = transaction.Id,
                AccountId = transaction.AccountId,
                MerchantName = transaction.MerchantName,
                MerchantCategory = transaction.MerchantCategory,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                CountryCode = transaction.CountryCode,
                OccurredAtUtc = transaction.OccurredAtUtc
            }, ct);

            if (published)
            {
                transaction.PublishedToKafkaUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
