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
public sealed class XpdSyncScheduler : IXpdSyncScheduler, IDisposable
{
    private readonly IXpdSyncService _xpdSyncService;
    private readonly ILogger<XpdSyncScheduler> _logger;

    private readonly bool _enabled;
    private readonly int _intervalMinutes;
    private readonly bool _runOnStartup;

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
        _logger = logger;

        _enabled = configuration.GetValue("XpdSync:Enabled", false);
        _intervalMinutes = configuration.GetValue("XpdSync:IntervalMinutes", 60);
        _runOnStartup = configuration.GetValue("XpdSync:RunOnStartup", false);

        if (_intervalMinutes < 1)
        {
            _logger.LogWarning(
                "XpdSync:IntervalMinutes must be >= 1, got {Value}. Falling back to 60.",
                _intervalMinutes);
            _intervalMinutes = 60;
        }
    }

    public void Start()
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
                    "Set XpdSync:Enabled=true in appsettings to enable hourly sync.");
                return;
            }

            TimeSpan dueTime = _runOnStartup ? StartupDelay : TimeSpan.FromMinutes(_intervalMinutes);
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

    public void Dispose()
    {
        Stop();
    }
}
