using Confluent.Kafka;
using FraudDetection.Api.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FraudDetection.Api.HealthChecks;

public class KafkaHealthCheck : IHealthCheck
{
    private readonly KafkaOptions _options;

    public KafkaHealthCheck(IOptions<KafkaOptions> options) => _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _options.BootstrapServers }).Build();
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return Task.FromResult(metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("No Kafka brokers reported"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka unreachable", ex));
        }
    }
}
