using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class StoresIntegrationTests : IntegrationTestBase
{
    public StoresIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetTwilioNumbers_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/stores/1/numbers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTwilioNumbers_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/stores/1/numbers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
