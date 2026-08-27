using System.Net;
using System.Net.Http.Json;
using FraudDetection.Api.DTOs;
using Xunit;

namespace FraudDetection.Api.Tests.Integration;

public class AuthControllerTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public AuthControllerTests(ApiTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ReturnsToken_ForValidCredentials()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "admin123"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("admin", body.Username);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForWrongPassword()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForUnknownUsername()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "nobody",
            Password = "admin123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
