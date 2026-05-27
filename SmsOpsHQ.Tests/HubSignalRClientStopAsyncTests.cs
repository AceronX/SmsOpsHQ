using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.HubClient;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Targets the new <see cref="IHubSignalRClient.StopAsync"/> contract that the
/// desktop App.xaml.cs.OnExit relies on. Failure modes here would cause the
/// WPF shutdown path to hang (bad) or skip the graceful goodbye and leave the
/// Hub waiting for keepalive timeout (annoying but recoverable). Either way
/// we want regression coverage.
///
/// We do NOT spin up a real SignalR server -- that would be slow and flaky.
/// The contract we lock in:
///   * StopAsync on a never-Start()ed client is a clean no-op (no throw).
///   * StopAsync twice in a row is a clean no-op.
///   * StopAsync respects the caller's cancellation token without hanging.
/// </summary>
public sealed class HubSignalRClientStopAsyncTests
{
    [Fact]
    public async Task StopAsync_OnNeverStartedClient_IsNoOp()
    {
        HubSignalRClient client = BuildClient();
        await client.StopAsync(TimeSpan.FromSeconds(1));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task StopAsync_CalledTwice_IsNoOpSecondTime()
    {
        HubSignalRClient client = BuildClient();
        await client.StopAsync(TimeSpan.FromSeconds(1));
        // Second call must not throw, must return promptly.
        Task secondCall = client.StopAsync(TimeSpan.FromSeconds(1));
        Task completed = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(secondCall, completed);
    }

    [Fact]
    public async Task StopAsync_RespectsCallerCancellation()
    {
        // The caller (App.xaml.cs.OnExit) doesn't pass a token today but the
        // overload exists; if it ever does, an already-cancelled token must
        // not deadlock the shutdown path.
        HubSignalRClient client = BuildClient();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await client.StopAsync(TimeSpan.FromSeconds(1), cts.Token);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_OnNeverStartedClient_DoesNotThrow()
    {
        HubSignalRClient client = BuildClient();
        await client.DisposeAsync();
    }

    private static HubSignalRClient BuildClient()
    {
        ServiceCollection services = new();
        // Empty container is fine; the no-op StopAsync paths don't resolve
        // any scoped services.
        ServiceProvider provider = services.BuildServiceProvider();
        return new HubSignalRClient(
            new FakeHeartbeatPusher(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HubSignalRClient>.Instance);
    }

    private sealed class FakeHeartbeatPusher : IHeartbeatPusher
    {
        public string HubUrl { get; set; } = "https://hub.test";
        public string StoreKey { get; set; } = "k";
        public string DeploymentId { get; set; } = "d";
        public int IntervalSeconds { get; set; } = 60;
        public bool IsConfigured => true;
        public void Start() { }
        public void Stop() { }
        public void Reload() { }
        public Task<bool> SendOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public HeartbeatPusherStatus GetStatus() => new();
        public Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HeartbeatPayload { DeploymentId = DeploymentId });
    }
}
