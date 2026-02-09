using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace SmsOpsHQ.Infrastructure.Services;

// Background reminder scheduler that fires a daily job at a configured time.
// Uses System.Threading.Timer for lightweight scheduling without external dependencies.
// Ported from Python reminder_scheduler.py (APScheduler).
public sealed class ReminderScheduler : IReminderScheduler, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderScheduler> _logger;
    private readonly int _scheduleHour;
    private readonly int _scheduleMinute;
    private readonly int _maxRemindersPerRun;
    private readonly int _dailySmsLimit;

    private Timer? _dailyTimer;
    private Timer? _midnightResetTimer;
    private bool _running;
    private int _dailySentCount;
    private DateOnly? _lastResetDate;
    private DateTime? _lastRunTime;
    private readonly object _lock = new();

    // Reminder intervals to process in order.
    private static readonly int[] ReminderIntervals = { -7, 0, 7, 14, 30 };

    private static readonly Dictionary<int, string> ReminderDescriptions = new()
    {
        { -7, "7 Days Before Expiration" },
        {  0, "Expiration Day" },
        {  7, "7 Days After Expiration" },
        { 14, "14 Days After Expiration" },
        { 30, "30 Days After Expiration (Final Notice)" }
    };

    public ReminderScheduler(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ReminderScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _scheduleHour = configuration.GetValue("Reminders:ScheduleHour", 10);
        _scheduleMinute = configuration.GetValue("Reminders:ScheduleMinute", 0);
        _maxRemindersPerRun = configuration.GetValue("Reminders:MaxPerRun", 200);
        _dailySmsLimit = configuration.GetValue("Reminders:DailyLimit", 500);
    }

    // ── Start / Stop ─────────────────────────────────────────────────

    public void Start()
    {
        lock (_lock)
        {
            if (_running)
            {
                _logger.LogWarning("Scheduler already running");
                return;
            }

            // Schedule daily job: calculate time until next fire.
            TimeSpan delayUntilNextRun = CalculateDelayUntilNextRun(_scheduleHour, _scheduleMinute);
            _dailyTimer = new Timer(OnDailyTimerFired, null, delayUntilNextRun, TimeSpan.FromHours(24));

            // Schedule midnight counter reset.
            TimeSpan delayUntilMidnight = CalculateDelayUntilNextRun(0, 0);
            _midnightResetTimer = new Timer(OnMidnightResetFired, null, delayUntilMidnight, TimeSpan.FromHours(24));

            _running = true;
            _logger.LogInformation(
                "Reminder scheduler started. Next run at {Hour}:{Minute:D2} (in {Delay})",
                _scheduleHour, _scheduleMinute, delayUntilNextRun);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running)
                return;

            _dailyTimer?.Dispose();
            _dailyTimer = null;
            _midnightResetTimer?.Dispose();
            _midnightResetTimer = null;
            _running = false;

            _logger.LogInformation("Reminder scheduler stopped");
        }
    }

    // ── Manual Run ───────────────────────────────────────────────────

    public async Task<AutoReminderResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteReminderRunAsync(cancellationToken);
    }

    // ── Status ───────────────────────────────────────────────────────

    public SchedulerStatus GetStatus()
    {
        lock (_lock)
        {
            string? nextRunTime = null;
            if (_running)
            {
                TimeSpan delay = CalculateDelayUntilNextRun(_scheduleHour, _scheduleMinute);
                DateTime nextRun = DateTime.Now.Add(delay);
                nextRunTime = nextRun.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return new SchedulerStatus
            {
                Running = _running,
                NextRunTime = nextRunTime,
                LastRunTime = _lastRunTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                DailySent = _dailySentCount,
                DailyLimit = _dailySmsLimit,
                LastResetDate = _lastResetDate?.ToString("yyyy-MM-dd")
            };
        }
    }

    // ── Timer Callbacks ──────────────────────────────────────────────

    private void OnDailyTimerFired(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                AutoReminderResult result = await ExecuteReminderRunAsync(CancellationToken.None);
                _logger.LogInformation(
                    "Scheduled reminder run complete: Sent={Sent} Failed={Failed} Skipped={Skipped}",
                    result.SentCount, result.FailedCount, result.SkippedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled reminder run failed");
            }
        });
    }

    private void OnMidnightResetFired(object? state)
    {
        ResetDailyCounter();
    }

    // ── Core Execution ───────────────────────────────────────────────

    private async Task<AutoReminderResult> ExecuteReminderRunAsync(CancellationToken cancellationToken)
    {
        ResetDailyCounter();

        if (_dailySentCount >= _dailySmsLimit)
        {
            _logger.LogWarning("Daily SMS limit ({Limit}) reached. Skipping run.", _dailySmsLimit);
            return new AutoReminderResult { Error = "daily_limit_reached" };
        }

        _logger.LogInformation("Starting automatic reminder run");

        AutoReminderResult results = new();
        DateTime today = DateTime.Now.Date;

        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IReminderService reminderService = scope.ServiceProvider.GetRequiredService<IReminderService>();

        foreach (int daysDiff in ReminderIntervals)
        {
            if (_dailySentCount >= _dailySmsLimit)
            {
                _logger.LogWarning("Daily limit reached during processing");
                break;
            }

            string reminderType = $"reminder_{daysDiff}";
            AutoReminderTypeResult typeResult = await ProcessReminderTypeAsync(
                db, reminderService, daysDiff, today, cancellationToken);

            results.SentCount += typeResult.Sent;
            results.FailedCount += typeResult.Failed;
            results.SkippedCount += typeResult.Skipped;
            results.ByType[reminderType] = typeResult;

            lock (_lock) { _dailySentCount += typeResult.Sent; }
        }

        lock (_lock) { _lastRunTime = DateTime.Now; }

        _logger.LogInformation(
            "Reminder run complete: Sent={Sent} Failed={Failed} Skipped={Skipped} DailyTotal={Daily}/{Limit}",
            results.SentCount, results.FailedCount, results.SkippedCount,
            _dailySentCount, _dailySmsLimit);

        return results;
    }

    private async Task<AutoReminderTypeResult> ProcessReminderTypeAsync(
        AppDbContext db, IReminderService reminderService,
        int daysDiff, DateTime today, CancellationToken cancellationToken)
    {
        DateTime targetDate = today.AddDays(-daysDiff);
        string targetDateStr = $"{targetDate.Month}/{targetDate.Day}/{targetDate.Year}";
        string reminderType = $"reminder_{daysDiff}";

        _logger.LogInformation(
            "Processing {Description}: target due date {TargetDate}",
            ReminderDescriptions.GetValueOrDefault(daysDiff, $"{daysDiff} days"),
            targetDateStr);

        AutoReminderTypeResult result = new();

        var tickets = await db.XpdTickets
            .AsNoTracking()
            .Where(t => t.Active == 1 && t.Type != 0 && t.DueDate == targetDateStr)
            .Join(db.XpdCustomers.AsNoTracking(),
                t => t.CustomerKey,
                c => c.Key,
                (t, c) => new
                {
                    t.Key,
                    t.CustomerKey,
                    t.TransNo,
                    t.DueDate,
                    Phone = c.ResPhone ?? c.BusPhone,
                    CustomerName = (c.FirstName ?? "") + " " + (c.LastName ?? "")
                })
            .Where(x => x.Phone != null)
            .Take(500)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} tickets for {DaysDiff} day reminders", tickets.Count, daysDiff);

        foreach (var ticket in tickets)
        {
            if (ticket.Phone is null)
            {
                result.Skipped++;
                continue;
            }

            // Check if already sent
            bool alreadySent = await db.SmsReminders
                .AsNoTracking()
                .AnyAsync(r => r.TicketKey == ticket.Key
                            && r.DueDate == ticket.DueDate
                            && r.ReminderType == reminderType
                            && r.Status == 1,
                    cancellationToken);

            if (alreadySent)
            {
                result.Skipped++;
                continue;
            }

            if (await reminderService.IsPhoneExcludedAsync(ticket.Phone, cancellationToken))
            {
                result.Skipped++;
                continue;
            }

            ReminderSendResult sendResult = await reminderService.SendReminderAsync(new SendReminderRequest
            {
                TicketKey = ticket.Key,
                CustomerKey = ticket.CustomerKey,
                Phone = ticket.Phone,
                TransNo = ticket.TransNo?.ToString() ?? "",
                DueDate = ticket.DueDate ?? "",
                DaysDiff = daysDiff,
                StoreId = 1
            }, cancellationToken);

            if (sendResult.Success)
            {
                result.Sent++;
                _logger.LogInformation(
                    "Sent to {Name} ({Phone}) - Ticket #{Trans}",
                    ticket.CustomerName?.Trim(), ticket.Phone, ticket.TransNo);
            }
            else
            {
                result.Failed++;
                _logger.LogWarning(
                    "Failed: {Name} ({Phone}) - {Message}",
                    ticket.CustomerName?.Trim(), ticket.Phone, sendResult.Message);
            }
        }

        _logger.LogInformation(
            "Type {DaysDiff}: {Sent} sent, {Failed} failed, {Skipped} skipped",
            daysDiff, result.Sent, result.Failed, result.Skipped);

        return result;
    }

    // ── Utility ──────────────────────────────────────────────────────

    private void ResetDailyCounter()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        lock (_lock)
        {
            if (_lastResetDate != today)
            {
                _dailySentCount = 0;
                _lastResetDate = today;
                _logger.LogInformation("Daily SMS counter reset for {Date}", today);
            }
        }
    }

    private static TimeSpan CalculateDelayUntilNextRun(int hour, int minute)
    {
        DateTime now = DateTime.Now;
        DateTime nextRun = new(now.Year, now.Month, now.Day, hour, minute, 0);
        if (nextRun <= now)
            nextRun = nextRun.AddDays(1);
        return nextRun - now;
    }

    public void Dispose()
    {
        Stop();
    }
}
