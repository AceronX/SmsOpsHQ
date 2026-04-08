namespace SmsOpsHQ.Core.Services;

// Contract for the automatic reminder scheduler.
// Runs a daily job to send reminders at a configured time.
public interface IReminderScheduler
{
    // Start the background scheduler.
    void Start();

    // Stop the background scheduler.
    void Stop();

    // Manually trigger one run of automatic reminders.
    Task<AutoReminderResult> RunNowAsync(CancellationToken cancellationToken = default);

    // Get current scheduler status.
    SchedulerStatus GetStatus();
}

public sealed class SchedulerStatus
{
    public bool Running { get; set; }
    public string? NextRunTime { get; set; }
    public string? LastRunTime { get; set; }
    public int DailySent { get; set; }
    public int DailyLimit { get; set; }
    public string? LastResetDate { get; set; }

    // Live progress for the current/last run.
    public bool IsRunInProgress { get; set; }
    public int RunSent { get; set; }
    public int RunFailed { get; set; }
    public int RunSkipped { get; set; }
    public int RunTotalEligible { get; set; }
    public string? RunCurrentPhase { get; set; }
}

public sealed class AutoReminderResult
{
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public Dictionary<string, AutoReminderTypeResult> ByType { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class AutoReminderTypeResult
{
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}
