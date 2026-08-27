using System.Text.Json;
using Confluent.Kafka;
using FraudDetection.Api.Configuration;
using FraudDetection.Api.Messaging;
using Microsoft.Extensions.Options;

namespace FraudDetection.Api.Services;

/// <summary>
/// Thin wrapper around a shared Kafka producer. Publishing is keyed by AccountId so
/// events for the same account land on the same partition and are processed in order —
/// the fraud rule engine's velocity check depends on that ordering.
/// </summary>
public class KafkaProducerService : IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly KafkaPipelineMetrics _metrics;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(
        IOptions<KafkaOptions> options,
        KafkaPipelineMetrics metrics,
        ILogger<KafkaProducerService> logger)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            MessageTimeoutMs = _options.MessageTimeoutMs,
            EnableIdempotence = true
        }).Build();
    }

    /// <summary>Publishes with a small inline retry. Returns false (never throws) if all
    /// attempts fail so callers can fall back to the outbox sweep instead of losing the event.</summary>
    public async Task<bool> PublishTransactionCreatedAsync(TransactionCreatedEventV1 payload, CancellationToken ct)
    {
        var envelope = new EventEnvelope<TransactionCreatedEventV1>
        {
            EventType = nameof(TransactionCreatedEventV1),
            EventVersion = 1,
            Payload = payload
        };
        var json = JsonSerializer.Serialize(envelope);

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await _producer.ProduceAsync(
                    _options.TransactionTopic,
                    new Message<string, string> { Key = payload.AccountId.ToString(), Value = json },
                    ct);

                _metrics.RecordProduced();
                _logger.LogInformation(
                    "Published TransactionCreated {TransactionId} to {Topic} [partition {Partition}, offset {Offset}]",
                    payload.TransactionId, result.Topic, result.Partition.Value, result.Offset.Value);
                return true;
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogWarning(ex,
                    "Kafka publish attempt {Attempt}/{MaxAttempts} failed for transaction {TransactionId}",
                    attempt, maxAttempts, payload.TransactionId);

                if (attempt == maxAttempts)
                {
                    _metrics.RecordFailed();
                    return false;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
