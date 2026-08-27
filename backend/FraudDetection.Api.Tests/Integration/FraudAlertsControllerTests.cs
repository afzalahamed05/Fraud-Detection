using System.Net;
using System.Net.Http.Json;
using FraudDetection.Api.Data;
using FraudDetection.Api.Models;
using Xunit;

namespace FraudDetection.Api.Tests.Integration;

public class FraudAlertsControllerTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public FraudAlertsControllerTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UpdateStatus_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.PatchAsJsonAsync($"/api/fraud-alerts/{Guid.NewGuid()}/status", AlertStatus.Reviewed);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAlert_ForUnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/fraud-alerts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTopTriggers_ReturnsEmptyList_WhenNoAlertsExist()
    {
        var response = await _client.GetAsync("/api/fraud-alerts/top-triggers");

        response.EnsureSuccessStatusCode();
        var triggers = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(triggers);
    }

    [Fact]
    public async Task GetTopTriggers_CountsTriggeredRulesAcrossAlerts()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var transaction = new Transaction
            {
                AccountId = Guid.NewGuid(),
                MerchantName = "Trigger Test",
                MerchantCategory = "Electronics",
                Amount = 9000,
                CountryCode = "RU",
                Status = TransactionStatus.Flagged
            };
            db.Transactions.Add(transaction);
            db.FraudAlerts.Add(new FraudAlert
            {
                TransactionId = transaction.Id,
                RiskScore = 80,
                Severity = AlertSeverity.Critical,
                Reason = "test",
                TriggeredRules = "[\"VeryLargeAmount\",\"RiskyCountry\"]"
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/fraud-alerts/top-triggers");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("VeryLargeAmount", body);
        Assert.Contains("RiskyCountry", body);
    }
}
