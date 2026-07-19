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
    public async Task PostLateCustomers_MixedCategories_ReturnsAllDistinctCategoriesAndHighestRisk()
    {
        const string query = @"
            SELECT
                1 AS Key,
                101 AS CustomerId,
                'Jamie' AS FirstName,
                'Mixed' AS LastName,
                '7185550100' AS ResPhone,
                '' AS BusPhone,
                '' AS CustomerNotes,
                11 AS TicketKey,
                555 AS TransNo,
                '2000-01-01' AS DueDate,
                10.0 AS CurrentBalance,
                20.0 AS Amount,
                '' AS TicketNotes,
                0 AS ForfeitCount,
                'Ring | Radio' AS Items,
                '' AS ItemNotes,
                ' JEWELRY | ELECTRONICS | jewelry | ' AS Category";

        HttpResponseMessage response = await PostAsync("/api/customers/late", new { query });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement customer = Assert.Single(document.RootElement.EnumerateArray());
        string[] categories = customer.GetProperty("categories")
            .EnumerateArray()
            .Select(category => category.GetString()!)
            .ToArray();

        Assert.Equal(new[] { "JEWELRY", "ELECTRONICS" }, categories);
        Assert.Equal("JEWELRY | ELECTRONICS", customer.GetProperty("category").GetString());
        Assert.Equal(50, customer.GetProperty("risk_score").GetInt32());
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
