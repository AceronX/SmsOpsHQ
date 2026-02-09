using System.Net;
using System.Text.Json;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public class QuarantineSyncIntegrationTests : IntegrationTestBase
{
    public QuarantineSyncIntegrationTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task GetQuarantined_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/quarantine/list?limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetQuarantineStats_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/quarantine/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSyncStatus_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/sync/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSyncProgress_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/sync/progress");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSyncCounts_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/sync/counts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StoreIsolation_HqAdminCanAccessAnyStore()
    {
        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=1&filter=all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StoreIsolation_UnauthenticatedReturns401()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StoreIsolation_StoreUser_CannotAccessOtherStore()
    {
        await Factory.SeedStoreUserAsync(2, "Store2", "store2user", "testpass123", "StoreAdmin");
        await AuthenticateAsUserAsync("store2user", "testpass123");

        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=1&filter=all");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StoreIsolation_StoreUser_CanAccessOwnStore()
    {
        await Factory.SeedStoreUserAsync(2, "Store2", "store2user2", "testpass123", "StoreAdmin");
        await AuthenticateAsUserAsync("store2user2", "testpass123");

        HttpResponseMessage response = await GetAsync("/api/inbox?store_id=2&filter=all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
