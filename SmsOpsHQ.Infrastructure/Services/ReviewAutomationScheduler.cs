using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Fires on an interval from persisted settings. If the callback is still running, the next tick is skipped.
public sealed class ReviewAutomationScheduler : IReviewAutomationScheduler, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReviewAutomationSettingsStore _settingsStore;
    private readonly ILogger<ReviewAutomationScheduler> _logger;
    private readonly SemaphoreSlim _overlapGate = new(1, 1);
    private readonly object _timerLock = new();

    private Timer? _timer;
    private bool _processStarted;
    private bool _firstTickAfterStart = true;

    public ReviewAutomationScheduler(
        IServiceScopeFactory scopeFactory,
        ReviewAutomationSettingsStore settingsStore,
        ILogger<ReviewAutomationScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public void Start()
    {
        if (_processStarted)
        {
            _logger.LogWarning("Review automation: Start() ignored (already initialized). Use ApplySettingsFromStore to reschedule.");
            return;
        }

        _processStarted = true;
        ApplySettingsFromStore();
    }

    public void Stop()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _logger.LogInformation("Review automation timer stopped.");
    }

    public void ApplySettingsFromStore()
    {
        ReviewAutomationSettings settings = _settingsStore.Load();

        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;

            if (!settings.Enabled)
            {
                _logger.LogInformation("Review automation: disabled in settings; timer not scheduled.");
                return;
            }

            TimeSpan interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
            TimeSpan due;

            if (_firstTickAfterStart && settings.RunOnStartup)
            {
                due = TimeSpan.Zero;
                _firstTickAfterStart = false;
            }
            else if (_firstTickAfterStart)
            {
                due = interval;
                _firstTickAfterStart = false;
            }
            else
            {
                // Rescheduled mid-flight (user saved settings): wait one full interval before next tick.
                due = interval;
            }

            _timer = new Timer(OnTimer, null, due, interval);

            _logger.LogInformation(
                "Review automation timer scheduled: every {Minutes} min, first tick in {Due}.",
                settings.IntervalMinutes,
                due);
        }
    }

    public ReviewAutomationSchedulerStatus GetStatus()
    {
        ReviewAutomationSettings s = _settingsStore.Load();
        lock (_timerLock)
        {
            return new ReviewAutomationSchedulerStatus
            {
                SchedulerRunning = _timer is not null,
                Enabled = s.Enabled,
                IntervalMinutes = s.IntervalMinutes,
                RunOnStartup = s.RunOnStartup,
                SettingsFilePath = _settingsStore.SettingsFilePath
            };
        }
    }

    public async Task<ReviewAutomationResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _overlapGate.WaitAsync(0, cancellationToken))
        {
            return new ReviewAutomationResult { Detail = "run_already_in_progress" };
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IReviewAutomationService job = scope.ServiceProvider.GetRequiredService<IReviewAutomationService>();
            return await job.ProcessNewTicketsAsync(cancellationToken);
        }
        finally
        {
            _overlapGate.Release();
        }
    }

    private void OnTimer(object? state)
    {
        _ = Task.Run(async () =>
        {
            if (!await _overlapGate.WaitAsync(0))
            {
                _logger.LogInformation("Review automation: skipped timer tick (previous run still in progress).");
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IReviewAutomationService job = scope.ServiceProvider.GetRequiredService<IReviewAutomationService>();
                ReviewAutomationResult r = await job.ProcessNewTicketsAsync(CancellationToken.None);
                _logger.LogInformation(
                    "Review automation run: Sent={Sent} Failed={Failed} Skipped={Skipped} Detail={Detail}",
                    r.Sent, r.Failed, r.Skipped, r.Detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Review automation scheduled run failed.");
            }
            finally
            {
                _overlapGate.Release();
            }
        });
    }

    public void Dispose()
    {
        Stop();
        _overlapGate.Dispose();
    }
}
