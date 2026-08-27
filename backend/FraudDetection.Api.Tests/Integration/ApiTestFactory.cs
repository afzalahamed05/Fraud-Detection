using FraudDetection.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FraudDetection.Api.Tests.Integration;

/// <summary>
/// Swaps the real Npgsql AppDbContext for a fresh EF InMemory database per factory instance,
/// and forces "Testing" environment so Program.cs skips the relational Migrate() call (see
/// Program.cs) -- InMemory doesn't support it, and each test seeds its own data anyway.
/// Kafka publish calls still hit the network (KafkaProducerService isn't mocked out) but
/// fail fast via the inline retry's own timeout, so tests stay correct without a live broker.
/// </summary>
public class ApiTestFactory : WebApplicationFactory<Program>
{
    public readonly string DatabaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "127.0.0.1:1", // nothing listens here -- fails fast, not "unreachable" fast
                ["Kafka:MessageTimeoutMs"] = "500",
                // Production reads this from an env var (see .env.example) -- appsettings.json
                // deliberately ships with an empty value, so tests need their own.
                ["Auth:JwtSecret"] = "test-only-secret-not-used-anywhere-real-0123456789"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            // Program.cs's UseNpgsql(...) already registered Npgsql's IDatabaseProvider etc. into
            // this service collection via TryAddEnumerable -- removing just the DbContextOptions
            // descriptor above doesn't remove those, so EF ends up with two competing providers
            // ("Only a single database provider can be registered"). Giving the InMemory provider
            // its own isolated internal service provider sidesteps the conflict entirely.
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(DatabaseName);
                options.UseInternalServiceProvider(inMemoryServiceProvider);
            });
        });
    }

    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }
}
