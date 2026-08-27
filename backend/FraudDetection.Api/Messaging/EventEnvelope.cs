namespace FraudDetection.Api.Messaging;

/// <summary>
/// Wraps every Kafka message payload so the schema can evolve without breaking older
/// consumers: bump <see cref="EventVersion"/> when the payload shape changes, and consumers
/// can branch on it instead of guessing from field presence.
/// </summary>
public class EventEnvelope<TPayload>
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public int EventVersion { get; set; } = 1;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public required TPayload Payload { get; set; }
}
