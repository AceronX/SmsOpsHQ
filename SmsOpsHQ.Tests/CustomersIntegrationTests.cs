using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class CustomersIntegrationTests : IntegrationTestBase
{
    public CustomersIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task SearchCustomers_NoResults_ReturnsEmptyArray()
    {
        JsonElement result = await GetJsonAsync("/api/customers/search?q=zznonexistent&limit=5");
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(0, result.GetArrayLength());
    }

    [Fact]
    public async Task GetCustomerContext_NonExistent_ReturnsNotFound()
    {
        HttpResponseMessage response = await GetAsync("/api/customer/99999/context");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostLateCustomers_ReturnsOk()
    {
        HttpResponseMessage response = await PostAsync("/api/customers/late", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPfxCustomers_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/customers/pfx?days=60");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SearchCustomers_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/customers/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
