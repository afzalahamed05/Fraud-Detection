namespace FraudDetection.Api.Configuration;

public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string TransactionTopic { get; set; } = "transactions.created";
    public string ConsumerGroupId { get; set; } = "fraud-detection-consumer";

    /// <summary>How long a queued message waits for broker ack before ProduceAsync throws.
    /// Overridden low in tests so a missing broker fails fast instead of hanging ~10s/attempt.</summary>
    public int MessageTimeoutMs { get; set; } = 10_000;
}
