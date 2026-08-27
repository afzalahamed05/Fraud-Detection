namespace FraudDetection.Api.Services;

/// <summary>
/// In-memory counters for the /api/health/pipeline endpoint. Reset on process restart —
/// this is a lightweight liveness/throughput signal, not a durable metrics store.
/// </summary>
public class KafkaPipelineMetrics
{
    private long _messagesProduced;
    private long _messagesConsumed;
    private long _messagesFailed;
    private DateTime? _lastConsumedAtUtc;

    public void RecordProduced() => Interlocked.Increment(ref _messagesProduced);

    public void RecordConsumed()
    {
        Interlocked.Increment(ref _messagesConsumed);
        _lastConsumedAtUtc = DateTime.UtcNow;
    }

    public void RecordFailed() => Interlocked.Increment(ref _messagesFailed);

    public long MessagesProduced => Interlocked.Read(ref _messagesProduced);
    public long MessagesConsumed => Interlocked.Read(ref _messagesConsumed);
    public long MessagesFailed => Interlocked.Read(ref _messagesFailed);
    public DateTime? LastConsumedAtUtc => _lastConsumedAtUtc;
}
