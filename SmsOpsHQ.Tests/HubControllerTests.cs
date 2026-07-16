using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.Controllers;
using SmsOpsHQ.Api.HubClient;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class HubControllerTests
{
    [Fact]
    public void Status_ReturnsCompleteEffectiveStateWithoutStoreKey()
    {
        const string secret = "must-not-appear";
        DateTime lastAttempt = new(2026, 7, 16, 14, 1, 0, DateTimeKind.Utc);
        DateTime lastSuccess = new(2026, 7, 16, 14, 0, 0, DateTimeKind.Utc);
        FakeHeartbeatPusher pusher = new()
        {
            StoreKey = secret,
            Status = new HeartbeatPusherStatus
            {
                Enabled = true,
                HubUrl = "https://hub.example.com",
                DeploymentId = "main-counter",
                IntervalSeconds = 60,
                LastAttemptUtc = lastAttempt,
                LastSuccessUtc = lastSuccess,
                LastError = "connection refused",
                SuccessCount = 12,
                FailureCount = 3
            }
        };
        FakeSignalRClient signalR = new() { IsConnectedValue = true };
        HubController controller = new(signalR, pusher, NullLogger<HubController>.Instance);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(controller.Status());
        HubStatusResponse response = Assert.IsType<HubStatusResponse>(ok.Value);

        Assert.True(response.Enabled);
        Assert.True(response.Configured);
        Assert.True(response.IsConnected);
        Assert.Equal("https://hub.example.com", response.HubUrl);
        Assert.Equal("main-counter", response.DeploymentId);
        Assert.Equal(60, response.IntervalSeconds);
        Assert.Equal(lastAttempt, response.LastAttemptUtc);
        Assert.Equal(lastSuccess, response.LastSuccessUtc);
        Assert.Equal("connection refused", response.LastError);
        Assert.Equal(12, response.SuccessCount);
        Assert.Equal(3, response.FailureCount);

        string json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreKey", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeHeartbeatPusher : IHeartbeatPusher
    {
        public HeartbeatPusherStatus Status { get; init; } = new();
        public string HubUrl => Status.HubUrl;
        public string StoreKey { get; init; } = string.Empty;
        public string DeploymentId => Status.DeploymentId;
        public int IntervalSeconds => Status.IntervalSeconds;
        public bool IsConfigured { get; init; } = true;
        public void Start() { }
        public void Stop() { }
        public void Reload() { }
        public void RecordConnectionError(string? error) { }
        public void RecordSignalRHeartbeatSuccess() { }
        public HeartbeatPusherStatus GetStatus() => Status;
        public Task<bool> SendOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HeartbeatPayload());
    }

    private sealed class FakeSignalRClient : IHubSignalRClient
    {
        public bool IsConnectedValue { get; init; }
        public bool IsConnected => IsConnectedValue;
        public void Start() { }
        public void Stop() { }
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
