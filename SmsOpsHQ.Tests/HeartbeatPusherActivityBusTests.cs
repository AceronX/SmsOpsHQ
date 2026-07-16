using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.HubClient;
using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the activity-driven immediate-heartbeat path on
/// <see cref="HeartbeatPusher"/>: when SMS-sent / SMS-received / thread-read
/// events arrive via <see cref="IStoreEventBus"/>, the pusher should debounce
/// and throttle them into a single immediate <c>SendOnceAsync</c> call instead
/// of a flood.
///
/// We can't easily verify the actual outbound HTTP POST without standing up
/// the full DbContext stack (BuildPayloadAsync queries the SQLite mirror), so
/// instead we count calls indirectly: each <c>SendOnceAsync</c> invocation
/// increments <c>FailureCount</c> when BuildPayloadAsync throws (no
/// AppDbContext registered in these unit tests). That's deterministic and
/// observable through the public <see cref="HeartbeatPusherStatus"/> snapshot.
///
/// <see cref="HeartbeatPusher.BurstDebounce"/> / <see cref="HeartbeatPusher.BurstThrottle"/>
/// are internal seams that the test shrinks to a few hundred ms so the suite
/// stays well under a second.
/// </summary>
public sealed class HeartbeatPusherActivityBusTests
{
    [Fact]
    public async Task NotifyActivity_FiresOneHeartbeatAfterDebounceWindow()
    {
        // Arrange: configured pusher (so OnActivity isn't a no-op).
        StoreEventBus bus = new();
        HeartbeatPusher pusher = BuildPusher(
            new()
            {
                ["Hub:Enabled"] = "true",
                ["Hub:Url"] = "http://localhost:9",
                ["Hub:StoreKey"] = "k",
                ["Hub:DeploymentId"] = "test-deployment",
                ["Hub:IntervalSeconds"] = "60"
            },
            bus);
        pusher.BurstDebounce = TimeSpan.FromMilliseconds(100);
        pusher.BurstThrottle = TimeSpan.FromMilliseconds(100);

        // Act: a burst of 5 events within ~5ms -- should collapse to ONE send.
        for (int i = 0; i < 5; i++)
            bus.NotifyActivity("sms.sent");

        await WaitForFailureCountAsync(pusher, 1, TimeSpan.FromSeconds(2));

        // Assert: exactly one SendOnceAsync ran for the entire burst.
        Assert.Equal(1, pusher.GetStatus().FailureCount);
    }

    [Fact]
    public async Task NotifyActivity_WhenHubDisabled_NeverFires()
    {
        StoreEventBus bus = new();
        HeartbeatPusher pusher = BuildPusher(
            new()
            {
                // The contract being tested: Hub disabled -> no immediate sends ever.
                ["Hub:Enabled"] = "false",
                ["Hub:Url"] = "http://localhost:9",
                ["Hub:StoreKey"] = "k"
            },
            bus);
        pusher.BurstDebounce = TimeSpan.FromMilliseconds(50);
        pusher.BurstThrottle = TimeSpan.FromMilliseconds(50);

        bus.NotifyActivity("sms.sent");
        bus.NotifyActivity("sms.received");

        // Generous wait -- a leaked subscription would have fired by now.
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        HeartbeatPusherStatus s = pusher.GetStatus();
        Assert.Equal(0, s.FailureCount);
        Assert.Equal(0, s.SuccessCount);
        Assert.Null(s.LastAttemptUtc);
    }

    [Fact]
    public async Task NotifyActivity_TwoSeparateBursts_RespectThrottleAndFireTwice()
    {
        StoreEventBus bus = new();
        HeartbeatPusher pusher = BuildPusher(
            new()
            {
                ["Hub:Enabled"] = "true",
                ["Hub:Url"] = "http://localhost:9",
                ["Hub:StoreKey"] = "k",
                ["Hub:DeploymentId"] = "test-deployment"
            },
            bus);
        pusher.BurstDebounce = TimeSpan.FromMilliseconds(100);
        pusher.BurstThrottle = TimeSpan.FromMilliseconds(200);

        // Burst 1
        bus.NotifyActivity("sms.sent");
        await WaitForFailureCountAsync(pusher, 1, TimeSpan.FromSeconds(2));

        // Wait past the throttle window so the second burst is allowed.
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // Burst 2
        bus.NotifyActivity("sms.received");
        await WaitForFailureCountAsync(pusher, 2, TimeSpan.FromSeconds(2));

        Assert.Equal(2, pusher.GetStatus().FailureCount);
    }

    [Fact]
    public async Task NotifyActivity_ContinuousFlood_CoalescesToThrottleCadence()
    {
        StoreEventBus bus = new();
        HeartbeatPusher pusher = BuildPusher(
            new()
            {
                ["Hub:Enabled"] = "true",
                ["Hub:Url"] = "http://localhost:9",
                ["Hub:StoreKey"] = "k",
                ["Hub:DeploymentId"] = "test-deployment"
            },
            bus);
        pusher.BurstDebounce = TimeSpan.FromMilliseconds(100);
        pusher.BurstThrottle = TimeSpan.FromMilliseconds(300);

        // Fire one event every 50ms for 1 second (20 events). With a 300ms
        // throttle, this should produce far fewer than 20 sends. We assert
        // <= 6 to allow for boundary/scheduler slop on slow CI runners.
        Task flood = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                bus.NotifyActivity("sms.sent");
                await Task.Delay(50);
            }
        });
        await flood;
        // Let the trailing debounce window drain.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        int sends = pusher.GetStatus().FailureCount;
        Assert.InRange(sends, 1, 6);
    }

    [Fact]
    public void Dispose_UnsubscribesFromBusAndStopsFiring()
    {
        StoreEventBus bus = new();
        HeartbeatPusher pusher = BuildPusher(
            new()
            {
                ["Hub:Enabled"] = "true",
                ["Hub:Url"] = "http://localhost:9",
                ["Hub:StoreKey"] = "k",
                ["Hub:DeploymentId"] = "test-deployment"
            },
            bus);
        pusher.BurstDebounce = TimeSpan.FromMilliseconds(50);
        pusher.BurstThrottle = TimeSpan.FromMilliseconds(50);

        pusher.Dispose();

        bus.NotifyActivity("sms.sent");
        // Brief wait -- a leftover subscription would fire within this window.
        Thread.Sleep(200);

        Assert.Equal(0, pusher.GetStatus().FailureCount);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static async Task WaitForFailureCountAsync(HeartbeatPusher pusher, int target, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (pusher.GetStatus().FailureCount >= target) return;
            await Task.Delay(20);
        }
    }

    private static HeartbeatPusher BuildPusher(
        Dictionary<string, string?> values,
        IStoreEventBus bus)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddHttpClient();
        ServiceProvider sp = services.BuildServiceProvider();

        HeartbeatPusher pusher = new(
            configuration,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HeartbeatPusher>.Instance,
            bus);

        // CRITICAL test isolation: the constructor's initial settings load
        // reads %AppData%\SmsOpsHQ\hub_config.json if it exists. On a developer
        // machine that file is real and contains Enabled=true, which would
        // override these in-memory test values. Force-reload from a non-existent
        // path so the loader falls through to our IConfiguration.
        string nonexistent = Path.Combine(Path.GetTempPath(),
            "SmsOpsHQ.Tests.NoOverlay." + Guid.NewGuid().ToString("N") + ".json");
        pusher.ReloadFromPath(nonexistent);

        return pusher;
    }
}
