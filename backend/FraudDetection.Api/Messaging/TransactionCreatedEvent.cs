namespace FraudDetection.Api.Messaging;

/// <summary>Payload (v1) published when a transaction is persisted and ready for fraud scoring.</summary>
public class TransactionCreatedEventV1
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantCategory { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
