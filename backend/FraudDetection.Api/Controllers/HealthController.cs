using Confluent.Kafka;
using FraudDetection.Api.Configuration;
using FraudDetection.Api.Data;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Models;
using FraudDetection.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FraudDetection.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly KafkaPipelineMetrics _metrics;
    private readonly KafkaOptions _kafkaOptions;

    public HealthController(AppDbContext db, KafkaPipelineMetrics metrics, IOptions<KafkaOptions> kafkaOptions)
    {
        _db = db;
        _metrics = metrics;
        _kafkaOptions = kafkaOptions.Value;
    }

    [HttpGet]
    public IActionResult GetLiveness() => Ok(new { status = "ok" });

    /// <summary>Snapshot of the Kafka streaming pipeline: is the broker reachable, how many
    /// transactions are still waiting on it, and rough throughput/latency since process start.</summary>
    [HttpGet("pipeline")]
    public async Task<ActionResult<PipelineHealthDto>> GetPipelineHealth(CancellationToken ct)
    {
        var stuckThreshold = DateTime.UtcNow.AddSeconds(-30);

        var pendingCount = await _db.Transactions.CountAsync(t => t.Status == TransactionStatus.Pending, ct);
        var unpublishedCount = await _db.Transactions.CountAsync(t => t.PublishedToKafkaUtc == null, ct);
        var stuckCount = await _db.Transactions.CountAsync(t =>
            t.Status == TransactionStatus.Pending &&
            t.PublishedToKafkaUtc != null &&
            t.ProcessedAtUtc == null &&
            t.PublishedToKafkaUtc < stuckThreshold, ct);
        var failedCount = await _db.Transactions.CountAsync(t => t.ProcessingError != null, ct);

        var recentProcessed = await _db.Transactions
            .Where(t => t.ProcessedAtUtc != null && t.PublishedToKafkaUtc != null)
            .OrderByDescending(t => t.ProcessedAtUtc)
            .Take(100)
            .Select(t => new { t.PublishedToKafkaUtc, t.ProcessedAtUtc })
            .ToListAsync(ct);

        var latenciesMs = recentProcessed
            .Select(t => (t.ProcessedAtUtc!.Value - t.PublishedToKafkaUtc!.Value).TotalMilliseconds)
            .ToList();

        return Ok(new PipelineHealthDto
        {
            KafkaConnected = IsKafkaReachable(),
            PendingCount = pendingCount,
            UnpublishedCount = unpublishedCount,
            StuckCount = stuckCount,
            FailedCount = failedCount,
            MessagesProduced = _metrics.MessagesProduced,
            MessagesConsumed = _metrics.MessagesConsumed,
            MessagesFailed = _metrics.MessagesFailed,
            LastConsumedAtUtc = _metrics.LastConsumedAtUtc,
            AvgProcessingLatencyMs = latenciesMs.Count > 0 ? latenciesMs.Average() : null
        });
    }

    private bool IsKafkaReachable()
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _kafkaOptions.BootstrapServers
            }).Build();

            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return metadata.Brokers.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
