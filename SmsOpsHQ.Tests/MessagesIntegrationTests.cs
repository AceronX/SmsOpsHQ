using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class MessagesIntegrationTests : IntegrationTestBase
{
    public MessagesIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetCategories_ReturnsStaticList()
    {
        JsonElement result = await GetJsonAsync("/api/messages/categories");

        Assert.True(result.TryGetProperty("categories", out JsonElement cats));
        Assert.True(cats.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task GetMessages_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/messages?store_id=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_ValidRequest_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/messages?store_id=1&limit=10&offset=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        JsonElement result = JsonDocument.Parse(body).RootElement;
        Assert.True(result.TryGetProperty("messages", out _));
        Assert.True(result.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task GetMessageCounts_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/messages/counts?store_id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        JsonElement result = JsonDocument.Parse(body).RootElement;
        Assert.True(result.TryGetProperty("counts", out JsonElement counts));
        Assert.True(counts.TryGetProperty("all", out _));
    }

    [Fact]
    public async Task GetMessages_InvalidCategory_Returns400()
    {
        HttpResponseMessage response = await GetAsync("/api/messages?store_id=1&category=invalid_cat");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
