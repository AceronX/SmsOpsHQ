using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Background scheduler that fires XpdSyncService.FullSyncAsync on a recurring
// interval (default hourly). Modeled on ReminderScheduler so the project keeps
// one consistent scheduler pattern.
//
// Concurrency: XpdSyncService already serializes runs with its own SemaphoreSlim.
// If a manual sync is in progress when the timer ticks, the call returns
// Success=false, Error="Sync already in progress" -- we treat that as a normal
// skip, not a failure to alarm on.
//
// Live reload: settings (Enabled / IntervalMinutes / RunOnStartup) are mutable.
// ReloadAsync re-reads the on-disk overlay written by the desktop UI's
// Settings -> XPD -> Hourly auto-sync panel and restarts the timer in-place
// (same UX as the Hub reload). See ApplySettings + ReadOverlay below.
public sealed class XpdSyncScheduler : IXpdSyncScheduler, IDisposable
{
    private readonly IXpdSyncService _xpdSyncService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<XpdSyncScheduler> _logger;

    // Mutable: ReloadAsync swaps these atomically (one writer holding _lock).
    // The volatile-of-struct semantics aren't strictly needed because every read
    // is gated by Start / Stop / GetStatus which take _lock too.
    private bool _enabled;
    private int _intervalMinutes;
    private bool _runOnStartup;

    // Initial delay when RunOnStartup=true. Gives the API a few seconds to
    // finish booting before we hold the SQLite connection for a long sync.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private Timer? _timer;
    private bool _running;
    private CancellationTokenSource? _runCts;

    private DateTime? _lastRunTime;
    private DateTime? _nextRunTime;
    private bool _lastRunSuccess;
    private string? _lastRunError;
    private int _totalRunCount;
    private int _successCount;
    private int _failureCount;
    private volatile bool _runInProgress;

    private readonly object _lock = new();

    public XpdSyncScheduler(
        IXpdSyncService xpdSyncService,
        IConfiguration configuration,
        ILogger<XpdSyncScheduler> logger)
    {
        _xpdSyncService = xpdSyncService;
        _configuration = configuration;
        _logger = logger;

        // Read settings via the same helper Reload uses so behavior is identical
        // between "startup" and "operator clicked Save". Overlay wins, then
        // appsettings, then safe defaults.
        ApplySettings(LoadSettings(GetDefaultOverlayPath()));
    }

    public void Start() => StartCore(useStartupDelay: true);

    // Underlying Start so ReloadAsync can opt out of the startup-delay path
    // (RunOnStartup only makes sense once, at API boot -- not on a live reload).
    private void StartCore(bool useStartupDelay)
    {
        lock (_lock)
        {
            if (_running)
            {
                _logger.LogInformation("XPD sync scheduler already running");
                return;
            }

            if (!_enabled)
            {
                _logger.LogInformation(
                    "XPD sync scheduler is disabled (XpdSync:Enabled=false). " +
                    "Enable it from Settings -> XPD -> Hourly auto-sync " +
                    "(or set XpdSync:Enabled=true in appsettings).");
                return;
            }

            TimeSpan dueTime = (useStartupDelay && _runOnStartup)
                ? StartupDelay
                : TimeSpan.FromMinutes(_intervalMinutes);
            TimeSpan period = TimeSpan.FromMinutes(_intervalMinutes);

            _runCts = new CancellationTokenSource();
            _timer = new Timer(OnTimerFired, null, dueTime, period);
            _nextRunTime = DateTime.Now.Add(dueTime);
            _running = true;

            _logger.LogInformation(
                "XPD sync scheduler started. Interval={Interval}m. First run at {NextRun} (in {Delay}). RunOnStartup={RunOnStartup}.",
                _intervalMinutes, _nextRunTime, dueTime, _runOnStartup);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running)
                return;

            _timer?.Dispose();
            _timer = null;

            try
            {
                _runCts?.Cancel();
            }
            catch
            {
                // ignore cancellation race
            }
            _runCts?.Dispose();
            _runCts = null;

            _nextRunTime = null;
            _running = false;

            _logger.LogInformation("XPD sync scheduler stopped");
        }
    }

    public async Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSyncAsync("manual", cancellationToken);
    }

    public XpdSyncSchedulerStatus GetStatus()
    {
        lock (_lock)
        {
            return new XpdSyncSchedulerStatus
            {
                Running = _running,
                IntervalMinutes = _enabled ? _intervalMinutes : 0,
                NextRunTime = _nextRunTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                LastRunTime = _lastRunTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                LastRunSuccess = _lastRunSuccess,
                LastRunError = _lastRunError,
                TotalRunCount = _totalRunCount,
                SuccessCount = _successCount,
                FailureCount = _failureCount,
                RunInProgress = _runInProgress
            };
        }
    }

    // Timer fires on a thread pool thread. Wrap in fire-and-forget Task so the
    // timer's own thread is freed immediately and exceptions can't leak.
    private void OnTimerFired(object? state)
    {
        CancellationToken token = _runCts?.Token ?? CancellationToken.None;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteSyncAsync("scheduled", token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Scheduled XPD sync canceled (scheduler stopping)");
            }
            catch (Exception ex)
            {
                // Defensive: ExecuteSyncAsync already catches, but never let an
                // exception escape the timer callback.
                _logger.LogError(ex, "Unhandled error in scheduled XPD sync");
            }
            finally
            {
                lock (_lock)
                {
                    if (_running)
                        _nextRunTime = DateTime.Now.AddMinutes(_intervalMinutes);
                }
            }
        }, token);
    }

    private async Task<SyncResult> ExecuteSyncAsync(string trigger, CancellationToken cancellationToken)
    {
        if (_runInProgress)
        {
            _logger.LogInformation(
                "XPD sync ({Trigger}) skipped: a sync is already running in this scheduler",
                trigger);
            return new SyncResult { Success = false, Error = "scheduler_run_in_progress" };
        }

        _runInProgress = true;
        DateTime startedAt = DateTime.Now;

        _logger.LogInformation("Starting XPD sync ({Trigger}) at {StartedAt}", trigger, startedAt);

        try
        {
            SyncResult result = await _xpdSyncService.FullSyncAsync(null, cancellationToken);

            lock (_lock)
            {
                _lastRunTime = DateTime.Now;
                _totalRunCount++;
                _lastRunSuccess = result.Success;
                _lastRunError = result.Success ? null : result.Error;

                if (result.Success)
                    _successCount++;
                else
                    _failureCount++;
            }

            if (result.Success)
            {
                _logger.LogInformation(
                    "XPD sync ({Trigger}) complete in {Duration:F1}s: " +
                    "Customers={Customers} Tickets={Tickets} Items={Items} Payments={Payments} PhoneIndex={Phones}",
                    trigger, result.DurationSeconds,
                    result.Synced.Customers, result.Synced.Tickets, result.Synced.Items,
                    result.Synced.Payments, result.Synced.PhoneIndex);
            }
            else
            {
                // "Sync already in progress" is a normal skip when an operator
                // ran a manual sync; log as info, not warning.
                bool isBenignSkip = string.Equals(result.Error, "Sync already in progress",
                    StringComparison.OrdinalIgnoreCase);

                if (isBenignSkip)
                {
                    _logger.LogInformation(
                        "XPD sync ({Trigger}) skipped: a manual sync is already running",
                        trigger);
                }
                else
                {
                    _logger.LogWarning(
                        "XPD sync ({Trigger}) failed: {Error}", trigger, result.Error);
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                _lastRunTime = DateTime.Now;
                _totalRunCount++;
                _failureCount++;
                _lastRunSuccess = false;
                _lastRunError = "canceled";
            }
            throw;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastRunTime = DateTime.Now;
                _totalRunCount++;
                _failureCount++;
                _lastRunSuccess = false;
                _lastRunError = ex.Message;
            }
            _logger.LogError(ex, "XPD sync ({Trigger}) threw an unhandled exception", trigger);
            return new SyncResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _runInProgress = false;
        }
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        // Read FIRST (so a malformed overlay doesn't leave us stopped with stale
        // settings). Then stop the existing timer and re-start if newly enabled.
        SchedulerSettings next = LoadSettings(GetDefaultOverlayPath());

        bool wasRunning;
        int previousInterval;
        lock (_lock)
        {
            wasRunning = _running;
            previousInterval = _intervalMinutes;
        }

        Stop();
        lock (_lock)
        {
            ApplySettings(next);
        }

        _logger.LogInformation(
            "XPD sync scheduler reloaded: enabled={Enabled}, interval={Interval}m, runOnStartup={RunOnStartup}",
            _enabled, _intervalMinutes, _runOnStartup);

        if (_enabled)
        {
            // On a live reload, RunOnStartup is a *startup* setting -- it
            // should NOT fire a sync 15s after the operator clicked Save.
            // Schedule using the regular interval delay instead.
            StartCore(useStartupDelay: false);
            if (wasRunning && previousInterval != _intervalMinutes)
                _logger.LogInformation(
                    "XPD sync interval changed: {Old}m -> {New}m", previousInterval, _intervalMinutes);
        }
        else
        {
            _logger.LogInformation("XPD sync auto-scheduler is now disabled (operator action).");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Stop();
    }

    // ── Settings load / apply ────────────────────────────────────────────
    //
    // Mirrors HeartbeatPusher's overlay pattern: the desktop UI writes
    // %AppData%\SmsOpsHQ\xpd_sync_config.json, which is layered onto
    // IConfiguration at app start via Program.cs's AddJsonFile call (so reads
    // through IConfiguration also reflect it). For live reload we re-read
    // the file directly because IConfiguration has reloadOnChange=false.

    private void ApplySettings(SchedulerSettings s)
    {
        _enabled = s.Enabled;
        int interval = s.IntervalMinutes;
        if (interval < 1)
        {
            _logger.LogWarning(
                "XpdSync:IntervalMinutes must be >= 1, got {Value}. Falling back to 60.", interval);
            interval = 60;
        }
        _intervalMinutes = interval;
        _runOnStartup = s.RunOnStartup;
    }

    /// <summary>Test seam: reload from an explicit overlay path.</summary>
    internal Task ReloadFromPathAsync(string overlayPath, CancellationToken cancellationToken = default)
    {
        SchedulerSettings next = LoadSettings(overlayPath);
        Stop();
        lock (_lock) { ApplySettings(next); }
        if (_enabled) StartCore(useStartupDelay: false);
        return Task.CompletedTask;
    }

    private SchedulerSettings LoadSettings(string overlayPath)
    {
        SchedulerSettings? overlay = TryReadOverlay(overlayPath);
        if (overlay is not null)
            return overlay;

        return new SchedulerSettings
        {
            Enabled = _configuration.GetValue("XpdSync:Enabled", false),
            IntervalMinutes = _configuration.GetValue("XpdSync:IntervalMinutes", 60),
            RunOnStartup = _configuration.GetValue("XpdSync:RunOnStartup", false)
        };
    }

    private SchedulerSettings? TryReadOverlay(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("XpdSync", out JsonElement xs)
                || xs.ValueKind != JsonValueKind.Object)
                return null;

            return new SchedulerSettings
            {
                Enabled = xs.TryGetProperty("Enabled", out JsonElement en)
                          && en.ValueKind == JsonValueKind.True,
                IntervalMinutes = xs.TryGetProperty("IntervalMinutes", out JsonElement im)
                                  && im.ValueKind == JsonValueKind.Number
                                  && im.TryGetInt32(out int iv) ? iv : 60,
                RunOnStartup = xs.TryGetProperty("RunOnStartup", out JsonElement ros)
                               && ros.ValueKind == JsonValueKind.True
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read XPD scheduler overlay at {Path}; falling back to IConfiguration", path);
            return null;
        }
    }

    private static string GetDefaultOverlayPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SmsOpsHQ", "xpd_sync_config.json");
    }

    private sealed class SchedulerSettings
    {
        public bool Enabled { get; init; }
        public int IntervalMinutes { get; init; } = 60;
        public bool RunOnStartup { get; init; }
    }
}
