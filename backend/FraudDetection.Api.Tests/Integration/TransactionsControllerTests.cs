using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Models;
using Xunit;

namespace FraudDetection.Api.Tests.Integration;

public class TransactionsControllerTests : IClassFixture<ApiTestFactory>
{
    // Matches Program.cs's AddJsonOptions(JsonStringEnumConverter) -- the API serializes enums
    // as strings, but System.Net.Http.Json's ReadFromJsonAsync default options don't know that.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, // API responses are camelCase (ASP.NET Core's MVC default)
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public TransactionsControllerTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTransaction_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = Guid.NewGuid(),
            MerchantName = "Test",
            MerchantCategory = "Retail",
            Amount = 10
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTransaction_WithAuth_PersistsAsPending()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = Guid.NewGuid(),
            MerchantName = "Integration Test Merchant",
            MerchantCategory = "Retail",
            Amount = 42.50m,
            Currency = "USD",
            CountryCode = "US"
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions);

        Assert.NotNull(created);
        Assert.Equal(TransactionStatus.Pending, created!.Status);
        Assert.Equal("Integration Test Merchant", created.MerchantName);
    }

    [Theory]
    [InlineData(0)] // below the [Range(0.01, ...)] minimum
    [InlineData(-5)]
    public async Task CreateTransaction_WithInvalidAmount_ReturnsBadRequest(decimal amount)
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = Guid.NewGuid(),
            MerchantName = "Test",
            MerchantCategory = "Retail",
            Amount = amount
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTransaction_MissingMerchantName_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();

        // MerchantName omitted -- [Required] should trip automatic ApiController validation.
        var payload = new { accountId = Guid.NewGuid(), merchantCategory = "Retail", amount = 10 };
        var response = await client.PostAsJsonAsync("/api/transactions", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTransaction_ForUnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_ReturnsPagedResult_WithCreatedTransaction()
    {
        var client = await AuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = Guid.NewGuid(),
            MerchantName = "Pagination Probe",
            MerchantCategory = "Retail",
            Amount = 15
        });

        var response = await _client.GetAsync("/api/transactions?page=1&pageSize=50");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(JsonOptions);

        Assert.NotNull(result);
        Assert.Contains(result!.Items, t => t.MerchantName == "Pagination Probe");
    }

    [Fact]
    public async Task GetTransactions_SearchFilter_MatchesMerchantNamePartially()
    {
        var client = await AuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = Guid.NewGuid(),
            MerchantName = "Unique Coffee Shop XYZ",
            MerchantCategory = "Dining",
            Amount = 8
        });

        var response = await _client.GetAsync("/api/transactions?search=Coffee Shop XYZ");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>(JsonOptions);

        Assert.NotNull(result);
        Assert.All(result!.Items, t => Assert.Contains("Coffee Shop XYZ", t.MerchantName));
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "admin123"
        });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }
}
