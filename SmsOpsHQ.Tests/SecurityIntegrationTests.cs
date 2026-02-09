using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class SecurityIntegrationTests : IntegrationTestBase
{
    public SecurityIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task Swagger_EndpointAvailable()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("info", out JsonElement info));
        Assert.Equal("SmsOps HQ API", info.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SwaggerUI_IsAccessible()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsOk()
    {
        ClearAuth();
        HttpResponseMessage response = await PostAsync("/api/auth/login", new { username = "admin", password = "password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("user", out _));
    }

    [Fact]
    public async Task UnauthenticatedRequests_Return401()
    {
        ClearAuth();
        string[] protectedEndpoints =
        [
            "/api/inbox?store_id=1",
            "/api/messages?store_id=1",
            "/api/templates?store_id=1",
            "/api/reminders/scheduler/status"
        ];

        foreach (string endpoint in protectedEndpoints)
        {
            HttpResponseMessage response = await GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotRequireAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidCredentials_DoesNotLeakInfo()
    {
        ClearAuth();
        HttpResponseMessage response = await PostAsync("/api/auth/login", new { username = "admin", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }
}
