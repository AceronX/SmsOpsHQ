using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class ThreadsIntegrationTests : IntegrationTestBase
{
    public ThreadsIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetInbox_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInbox_ValidRequest_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=1&filter=all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetThread_NonExistent_Returns404()
    {
        HttpResponseMessage response = await GetAsync("/api/thread/99999?store_id=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteThread_NonExistent_Returns404()
    {
        HttpResponseMessage response = await DeleteAsync("/api/thread/99999?store_id=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAllConversations_ReturnsOk()
    {
        HttpResponseMessage response = await DeleteAsync("/api/conversations?store_id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetThreadsBulk_InvalidIds_Returns400()
    {
        HttpResponseMessage response = await GetAsync("/api/threads/bulk?store_id=1&thread_ids=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetThreadsBulk_EmptyIds_Returns400()
    {
        HttpResponseMessage response = await GetAsync("/api/threads/bulk?store_id=1&thread_ids=");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMessagesBulk_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/messages/bulk?store_id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetInbox_FilterVariants_AllReturnOk()
    {
        string[] filters = ["all", "open", "unread", "closed"];
        foreach (string filter in filters)
        {
            HttpResponseMessage response = await GetAsync($"/api/inbox?store_id=1&filter={filter}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
