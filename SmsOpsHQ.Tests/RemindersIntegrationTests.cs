using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class RemindersIntegrationTests : IntegrationTestBase
{
    public RemindersIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetSchedulerStatus_ReturnsOk()
    {
        JsonElement result = await GetJsonAsync("/api/reminders/scheduler/status");
        Assert.True(result.TryGetProperty("running", out _));
    }

    [Fact]
    public async Task GetStatistics_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/reminders/statistics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRecentReminders_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/reminders/recent?limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSentReminders_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/reminders/sent?limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CheckExcluded_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/reminders/excluded/+15551234567");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SchedulerStartStop_ReturnsOk()
    {
        HttpResponseMessage startResponse = await PostAsync("/api/reminders/scheduler/start", new { });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        HttpResponseMessage stopResponse = await PostAsync("/api/reminders/scheduler/stop", new { });
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
    }

    [Fact]
    public async Task ExcludePhone_ReturnsOk()
    {
        HttpResponseMessage response = await PostAsync("/api/reminders/exclude", new
        {
            phone = "+15559999999",
            reason = "Test exclusion"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedulerStatus_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/reminders/scheduler/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
