namespace SmsOpsHQ.Core.Services;

// Contract for the automatic XPD sync scheduler.
// Runs FullSyncAsync on a recurring interval (typically hourly) so the
// SQLite mirror of XPawn stays fresh without operator intervention.
public interface IXpdSyncScheduler
{
    // Start the recurring timer. Idempotent: a no-op if already running.
    void Start();

    // Stop the recurring timer. Cancels the next tick; an in-flight sync
    // is allowed to finish (cancellation is cooperative).
    void Stop();

    // Fire a sync immediately, outside the recurring schedule. Returns
    // the SyncResult from XpdSyncService. Does not affect the timer.
    Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default);

    // Snapshot of scheduler state for status endpoints / HQ console.
    XpdSyncSchedulerStatus GetStatus();
}

// Status snapshot of the XPD sync scheduler.
public sealed class XpdSyncSchedulerStatus
{
    // True after Start() until Stop() (or Dispose).
    public bool Running { get; set; }

    // Configured cadence. 0 if disabled in config.
    public int IntervalMinutes { get; set; }

    // Wall-clock time the next tick will fire (formatted "yyyy-MM-dd HH:mm:ss").
    public string? NextRunTime { get; set; }

    // Wall-clock time of the last completed tick.
    public string? LastRunTime { get; set; }

    // True if the last tick reported Success=true.
    public bool LastRunSuccess { get; set; }

    // Error string from the last failed tick (null on success or before first run).
    public string? LastRunError { get; set; }

    // Total ticks executed since Start() (success + failure).
    public int TotalRunCount { get; set; }

    // Ticks where Success=true.
    public int SuccessCount { get; set; }

    // Ticks where Success=false (includes "already in progress" skips).
    public int FailureCount { get; set; }

    // True while a tick (or RunNowAsync) is currently executing.
    public bool RunInProgress { get; set; }
}
