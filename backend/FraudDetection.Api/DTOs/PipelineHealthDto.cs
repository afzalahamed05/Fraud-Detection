namespace FraudDetection.Api.DTOs;

public class PipelineHealthDto
{
    public bool KafkaConnected { get; set; }
    public int PendingCount { get; set; }
    public int UnpublishedCount { get; set; }
    public int StuckCount { get; set; }
    public int FailedCount { get; set; }
    public long MessagesProduced { get; set; }
    public long MessagesConsumed { get; set; }
    public long MessagesFailed { get; set; }
    public DateTime? LastConsumedAtUtc { get; set; }
    public double? AvgProcessingLatencyMs { get; set; }
}
