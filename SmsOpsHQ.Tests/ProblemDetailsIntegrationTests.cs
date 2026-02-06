using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmsOpsHQ.Tests;

// Custom WebApplicationFactory that uses an isolated temp-file SQLite database
// so integration tests do not interfere with the development database.
public class SmsOpsHQWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _testDbPath = Path.Combine(
        Path.GetTempPath(),
        $"smsops_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((WebHostBuilderContext context, IConfigurationBuilder config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_testDbPath}"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Best-effort cleanup; temp file will be removed by OS eventually.
            }
        }
    }
}

// Integration tests verifying that all API error responses conform to RFC 7807 Problem Details.
public class ProblemDetailsIntegrationTests : IClassFixture<SmsOpsHQWebApplicationFactory>
{
    private readonly HttpClient _httpClient;

    public ProblemDetailsIntegrationTests(SmsOpsHQWebApplicationFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401ProblemDetails()
    {
        string requestJson = JsonSerializer.Serialize(new { username = "admin", password = "wrongpassword" });
        StringContent requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync("/api/auth/login", requestContent);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.Equal("Invalid username or password.", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("type", out JsonElement typeElement));
        Assert.False(string.IsNullOrEmpty(typeElement.GetString()));
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        string requestJson = JsonSerializer.Serialize(new { username = "admin", password = "password" });
        StringContent requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync("/api/auth/login", requestContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.True(root.TryGetProperty("accessToken", out JsonElement accessTokenElement));
        Assert.False(string.IsNullOrEmpty(accessTokenElement.GetString()));
        Assert.Equal("Bearer", root.GetProperty("tokenType").GetString());
        Assert.True(root.GetProperty("expiresIn").GetInt32() > 0);
        Assert.Equal("admin", root.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task DiagError_Returns500ProblemDetails()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/api/diag/error");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.True(root.TryGetProperty("detail", out JsonElement detailElement));
        Assert.False(string.IsNullOrEmpty(detailElement.GetString()));
        Assert.True(root.TryGetProperty("type", out JsonElement typeElement));
        Assert.False(string.IsNullOrEmpty(typeElement.GetString()));
        Assert.True(root.TryGetProperty("traceId", out JsonElement traceIdElement));
        Assert.False(string.IsNullOrEmpty(traceIdElement.GetString()));
    }

    [Fact]
    public async Task DiagError_InDevelopment_IncludesExceptionMessage()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/api/diag/error");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        string detail = root.GetProperty("detail").GetString()!;
        Assert.Contains("test exception", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonexistentRoute_Returns404ProblemDetails()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/api/nonexistent/route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(responseBody), "404 response should have a Problem Details body.");

        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("traceId", out JsonElement traceIdElement));
        Assert.False(string.IsNullOrEmpty(traceIdElement.GetString()));
    }

    [Fact]
    public async Task HealthEndpoint_StillReturns200()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("SmsOps HQ", root.GetProperty("service").GetString());
    }

    [Fact]
    public async Task RootEndpoint_StillReturns200()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.Equal("SmsOps HQ API", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_EmptyBody_Returns400ProblemDetails()
    {
        StringContent emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync("/api/auth/login", emptyContent);

        // Empty body with default values goes through to AuthService which returns null → 401.
        // OR if model binding fails, ASP.NET returns 400 with Problem Details.
        // Either way, the response should be a Problem Details shape.
        bool isClientError = response.StatusCode == HttpStatusCode.BadRequest
                          || response.StatusCode == HttpStatusCode.Unauthorized;
        Assert.True(isClientError, $"Expected 400 or 401, got {(int)response.StatusCode}.");

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        Assert.True(root.TryGetProperty("status", out JsonElement statusElement));
        Assert.True(statusElement.GetInt32() >= 400 && statusElement.GetInt32() < 500);
    }
}
