using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.HubClient;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the M5 "Reload" path on <see cref="HeartbeatPusher"/>. The reload
/// is what lets the desktop UI's Settings &gt; HQ Hub &gt; Save take effect
/// without an app restart -- it re-reads the on-disk overlay file written by
/// <c>HubConfigService</c> and swaps the pusher's cached fields.
///
/// We use the <c>ReloadFromPath</c> test seam so the tests don't have to write
/// into the real <c>%AppData%</c>.
/// </summary>
public sealed class HeartbeatPusherReloadTests : IDisposable
{
    private readonly string _tempOverlayPath;

    public HeartbeatPusherReloadTests()
    {
        _tempOverlayPath = Path.Combine(
            Path.GetTempPath(),
            "SmsOpsHQ.Tests.HubReload." + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempOverlayPath))
        {
            try { File.Delete(_tempOverlayPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Reload_OverlayMissing_FallsBackToAppsettings()
    {
        // appsettings-style config simulating a deployment-baked Hub section.
        HeartbeatPusher pusher = BuildPusher(new()
        {
            ["Hub:Enabled"] = "true",
            ["Hub:Url"] = "https://hq-from-appsettings.example.com",
            ["Hub:StoreKey"] = "appsettings-key",
            ["Hub:DeploymentId"] = "appsettings-dep",
            ["Hub:IntervalSeconds"] = "45"
        });

        // No overlay file at the path -> Reload must use IConfiguration.
        Assert.False(File.Exists(_tempOverlayPath));
        pusher.ReloadFromPath(_tempOverlayPath);

        Assert.True(pusher.IsConfigured);
        Assert.Equal("https://hq-from-appsettings.example.com", pusher.HubUrl);
        Assert.Equal("appsettings-key", pusher.StoreKey);
        Assert.Equal("appsettings-dep", pusher.DeploymentId);
        Assert.Equal(45, pusher.IntervalSeconds);
    }

    [Fact]
    public void Reload_OverlayPresent_TakesPrecedenceOverAppsettings()
    {
        // Both layers have values -- overlay (what the desktop UI just saved)
        // must win, exactly like Program.cs's AddJsonFile overlay does at startup.
        HeartbeatPusher pusher = BuildPusher(new()
        {
            ["Hub:Enabled"] = "false",
            ["Hub:Url"] = "https://will-be-overridden.example.com",
            ["Hub:StoreKey"] = "old-key",
            ["Hub:DeploymentId"] = "old-dep",
            ["Hub:IntervalSeconds"] = "120"
        });

        WriteOverlay(new
        {
            Hub = new
            {
                Enabled = true,
                Url = "https://kingvoice.ngrok.app",
                StoreKey = "fresh-store-key",
                DeploymentId = "main-counter-pc",
                IntervalSeconds = 60
            }
        });

        pusher.ReloadFromPath(_tempOverlayPath);

        Assert.True(pusher.IsConfigured);
        Assert.Equal("https://kingvoice.ngrok.app", pusher.HubUrl);
        Assert.Equal("fresh-store-key", pusher.StoreKey);
        Assert.Equal("main-counter-pc", pusher.DeploymentId);
        Assert.Equal(60, pusher.IntervalSeconds);
    }

    [Fact]
    public void Reload_OverlayDisablesHub_IsConfiguredBecomesFalse()
    {
        HeartbeatPusher pusher = BuildPusher(new()
        {
            ["Hub:Enabled"] = "true",
            ["Hub:Url"] = "https://hq.example.com",
            ["Hub:StoreKey"] = "still-here",
            ["Hub:IntervalSeconds"] = "60"
        });
        Assert.True(pusher.IsConfigured);

        WriteOverlay(new
        {
            Hub = new
            {
                Enabled = false,
                Url = "https://hq.example.com",
                StoreKey = "still-here",
                DeploymentId = "",
                IntervalSeconds = 60
            }
        });

        pusher.ReloadFromPath(_tempOverlayPath);

        // URL/key still populated, but Enabled=false flips IsConfigured off,
        // which is what HubSignalRClient.Start() checks to decide whether to
        // connect at all. This is the "operator turned reporting off" path.
        Assert.False(pusher.IsConfigured);
        Assert.Equal("https://hq.example.com", pusher.HubUrl);
        Assert.Equal("still-here", pusher.StoreKey);
    }

    [Fact]
    public void Reload_OverlayTrimsTrailingSlashFromUrl()
    {
        HeartbeatPusher pusher = BuildPusher(new());
        WriteOverlay(new
        {
            Hub = new
            {
                Enabled = true,
                Url = "https://hq.example.com/",  // trailing slash on purpose
                StoreKey = "k",
                DeploymentId = "d",
                IntervalSeconds = 60
            }
        });

        pusher.ReloadFromPath(_tempOverlayPath);

        // The pusher canonicalises by stripping the trailing slash so the
        // REST call's URL concat ("/api/heartbeat") doesn't end up as "//".
        Assert.Equal("https://hq.example.com", pusher.HubUrl);
    }

    [Fact]
    public void Reload_OverlayIntervalBelowFloor_IsClampedToTen()
    {
        HeartbeatPusher pusher = BuildPusher(new());
        WriteOverlay(new
        {
            Hub = new
            {
                Enabled = true,
                Url = "https://hq.example.com",
                StoreKey = "k",
                DeploymentId = "d",
                IntervalSeconds = 1  // pathological
            }
        });

        pusher.ReloadFromPath(_tempOverlayPath);
        Assert.Equal(10, pusher.IntervalSeconds);
    }

    [Fact]
    public void Reload_OverlayUnreadable_FallsBackToAppsettings()
    {
        HeartbeatPusher pusher = BuildPusher(new()
        {
            ["Hub:Enabled"] = "true",
            ["Hub:Url"] = "https://fallback.example.com",
            ["Hub:StoreKey"] = "fallback-key",
            ["Hub:IntervalSeconds"] = "60"
        });

        // Write garbage to the overlay path so JsonDocument.Parse throws.
        File.WriteAllText(_tempOverlayPath, "{ this is not valid json");

        pusher.ReloadFromPath(_tempOverlayPath);

        Assert.True(pusher.IsConfigured);
        Assert.Equal("https://fallback.example.com", pusher.HubUrl);
        Assert.Equal("fallback-key", pusher.StoreKey);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static HeartbeatPusher BuildPusher(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddHttpClient();
        ServiceProvider sp = services.BuildServiceProvider();

        return new HeartbeatPusher(
            configuration,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HeartbeatPusher>.Instance);
    }

    private void WriteOverlay(object document)
    {
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_tempOverlayPath, json);
    }
}
