using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class TemplatesIntegrationTests : IntegrationTestBase
{
    public TemplatesIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetTemplates_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/templates?store_id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_Valid_ReturnsOk()
    {
        JsonElement result = await PostJsonAsync("/api/templates", new
        {
            name = "Test Template",
            body = "Hello {name}, your ticket is due."
        });

        Assert.True(result.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task TemplateCrud_FullLifecycle()
    {
        // Create
        JsonElement created = await PostJsonAsync("/api/templates", new
        {
            name = "CRUD Test",
            body = "CRUD body text"
        });

        int templateId = created.GetProperty("id").GetInt32();
        Assert.True(templateId > 0, "Template ID should be positive after create");

        // List
        HttpResponseMessage listResponse = await GetAsync("/api/templates?store_id=1");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        // Update
        HttpResponseMessage updateResponse = await PutAsync($"/api/templates/{templateId}", new
        {
            name = "Updated CRUD Test",
            body = "Updated body"
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Delete
        HttpResponseMessage deleteResponse = await DeleteAsync($"/api/templates/{templateId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetTemplates_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/templates?store_id=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
