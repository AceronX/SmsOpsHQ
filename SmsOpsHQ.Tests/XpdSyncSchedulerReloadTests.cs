using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the live-reload path on <see cref="XpdSyncScheduler"/>. The reload
/// is what lets the desktop UI's Settings -&gt; XPD -&gt; "Hourly auto-sync"
/// panel take effect without an app restart -- it re-reads the on-disk overlay
/// (<c>xpd_sync_config.json</c>) written by <c>XpdSyncSchedulerConfigService</c>
/// and restarts the timer.
///
/// We use the internal <see cref="XpdSyncScheduler.ReloadFromPathAsync"/>
/// test seam so the tests don't have to write into the real <c>%AppData%</c>
/// (which would pollute the developer's machine and break in CI).
/// </summary>
public sealed class XpdSyncSchedulerReloadTests : IDisposable
{
    private readonly string _tempOverlayPath;

    public XpdSyncSchedulerReloadTests()
    {
        _tempOverlayPath = Path.Combine(
            Path.GetTempPath(),
            "SmsOpsHQ.Tests.XpdSched." + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempOverlayPath))
        {
            try { File.Delete(_tempOverlayPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Reload_OverlayMissing_FallsBackToAppsettings()
    {
        // appsettings-style config: hourly sync enabled at 30 min.
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "true",
            ["XpdSync:IntervalMinutes"] = "30",
            ["XpdSync:RunOnStartup"] = "false"
        });

        Assert.False(File.Exists(_tempOverlayPath));
        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        XpdSyncSchedulerStatus s = scheduler.GetStatus();
        Assert.True(s.Running);
        Assert.Equal(30, s.IntervalMinutes);

        scheduler.Dispose();
    }

    [Fact]
    public async Task Reload_OverlayPresent_TakesPrecedenceOverAppsettings()
    {
        // appsettings says OFF, overlay (what the desktop UI just saved) says
        // ON with a 5-minute interval. Overlay must win, exactly like
        // Program.cs's AddJsonFile overlay does at startup.
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "false",
            ["XpdSync:IntervalMinutes"] = "60",
            ["XpdSync:RunOnStartup"] = "false"
        });

        WriteOverlay(new
        {
            XpdSync = new
            {
                Enabled = true,
                IntervalMinutes = 5,
                RunOnStartup = false
            }
        });

        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        XpdSyncSchedulerStatus s = scheduler.GetStatus();
        Assert.True(s.Running);
        Assert.Equal(5, s.IntervalMinutes);

        scheduler.Dispose();
    }

    [Fact]
    public async Task Reload_OverlayDisablesIt_StopsRunning()
    {
        // Initial: enabled hourly. After reload from overlay that disables it,
        // the timer must be stopped and status must reflect "not running".
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "true",
            ["XpdSync:IntervalMinutes"] = "60"
        });
        scheduler.Start();
        Assert.True(scheduler.GetStatus().Running);

        WriteOverlay(new
        {
            XpdSync = new
            {
                Enabled = false,
                IntervalMinutes = 60,
                RunOnStartup = false
            }
        });
        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        Assert.False(scheduler.GetStatus().Running);

        scheduler.Dispose();
    }

    [Fact]
    public async Task Reload_ChangesIntervalOnly_RestartsTimer()
    {
        // Going from 60m -> 15m must reset the next-run time to ~15m out
        // (not still pointing at the 60m mark from the old timer).
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "true",
            ["XpdSync:IntervalMinutes"] = "60"
        });
        scheduler.Start();

        WriteOverlay(new
        {
            XpdSync = new
            {
                Enabled = true,
                IntervalMinutes = 15,
                RunOnStartup = false
            }
        });
        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        XpdSyncSchedulerStatus s = scheduler.GetStatus();
        Assert.True(s.Running);
        Assert.Equal(15, s.IntervalMinutes);
        // NextRunTime should be ~15 minutes out, NOT ~60. The string format is
        // "yyyy-MM-dd HH:mm:ss" local; parse and verify.
        Assert.NotNull(s.NextRunTime);
        DateTime next = DateTime.Parse(s.NextRunTime!);
        TimeSpan delta = next - DateTime.Now;
        Assert.InRange(delta.TotalMinutes, 14.5, 15.5);

        scheduler.Dispose();
    }

    [Fact]
    public async Task Reload_PathologicalInterval_ClampsToSafeDefault()
    {
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "true",
            ["XpdSync:IntervalMinutes"] = "60"
        });

        // Garbage zero from the overlay should fall back to 60 (per
        // ApplySettings clamp), not crash and not produce a 0-minute timer.
        WriteOverlay(new
        {
            XpdSync = new
            {
                Enabled = true,
                IntervalMinutes = 0,
                RunOnStartup = false
            }
        });
        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        XpdSyncSchedulerStatus s = scheduler.GetStatus();
        Assert.True(s.Running);
        Assert.Equal(60, s.IntervalMinutes);

        scheduler.Dispose();
    }

    [Fact]
    public async Task Reload_OverlayUnreadable_FallsBackToAppsettings()
    {
        XpdSyncScheduler scheduler = BuildScheduler(new()
        {
            ["XpdSync:Enabled"] = "true",
            ["XpdSync:IntervalMinutes"] = "45"
        });

        File.WriteAllText(_tempOverlayPath, "{ this is not valid json");

        await scheduler.ReloadFromPathAsync(_tempOverlayPath);

        XpdSyncSchedulerStatus s = scheduler.GetStatus();
        // Falls back to appsettings -> still enabled at 45.
        Assert.True(s.Running);
        Assert.Equal(45, s.IntervalMinutes);

        scheduler.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static XpdSyncScheduler BuildScheduler(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new XpdSyncScheduler(
            new NoopXpdSyncService(),
            configuration,
            NullLogger<XpdSyncScheduler>.Instance);
    }

    private void WriteOverlay(object document)
    {
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_tempOverlayPath, json);
    }

    /// <summary>
    /// Standin for IXpdSyncService -- the scheduler tests verify timer config
    /// behavior, not what happens when the timer fires (covered elsewhere).
    /// Calling FullSyncAsync here would never actually happen with intervals
    /// of 15-60 MINUTES inside a unit-test window of milliseconds.
    /// </summary>
    private sealed class NoopXpdSyncService : IXpdSyncService
    {
        public bool TryMarkSyncStarting() => true;
        public Task<SyncResult> FullSyncAsync(SyncRunOptions? overrides = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new SyncResult { Success = true });
        public SyncProgress GetProgress() => new();
        public SyncStatus GetSyncStatus() => new();
        public Task<Dictionary<string, int>> GetSqliteCountsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<string, int>());
    }
}
