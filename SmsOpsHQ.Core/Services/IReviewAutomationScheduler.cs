namespace SmsOpsHQ.Core.Services;

// Timer-driven review automation; skips a tick if the previous run is still executing.
// Interval and on/off are driven by persisted settings (Desktop + API).
public interface IReviewAutomationScheduler
{
    void Start();

    void Stop();

    /// <summary>Reload settings from disk and reschedule the timer (call after saving settings).</summary>
    void ApplySettingsFromStore();

    ReviewAutomationSchedulerStatus GetStatus();

    Task<ReviewAutomationResult> RunNowAsync(CancellationToken cancellationToken = default);
}
