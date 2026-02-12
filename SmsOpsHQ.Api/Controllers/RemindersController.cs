using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

// Endpoints for the pawn reminder system: send, batch, schedule, history, exclusions.
// 17 endpoints ported from Python routes_reminders.py.
[ApiController]
[Authorize]
[Route("api/reminders")]
public sealed class RemindersController : ControllerBase
{
    private readonly IReminderService _reminderService;
    private readonly IReminderScheduler _scheduler;
    private readonly ILogger<RemindersController> _logger;

    public RemindersController(
        IReminderService reminderService,
        IReminderScheduler scheduler,
        ILogger<RemindersController> logger)
    {
        _reminderService = reminderService;
        _scheduler = scheduler;
        _logger = logger;
    }

    // ── 1. POST /api/reminders/send ──────────────────────────────────

    [HttpPost("send")]
    public async Task<IActionResult> SendReminder(
        [FromBody] SendReminderApiRequest body, CancellationToken cancellationToken)
    {
        ReminderSendResult result = await _reminderService.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = body.TicketKey,
            CustomerKey = body.CustomerKey,
            Phone = body.Phone,
            TransNo = body.TransNo,
            DueDate = body.DueDate,
            DaysDiff = body.DaysDiff,
            UserId = User.GetUserId(),
            StoreId = User.GetStoreId() ?? 1
        }, cancellationToken);

        return Ok(result);
    }

    // ── 2. POST /api/reminders/batch ─────────────────────────────────

    [HttpPost("batch")]
    public async Task<IActionResult> RunBatchReminders(
        [FromBody] BatchReminderApiRequest body, CancellationToken cancellationToken)
    {
        BatchReminderResult result = await _reminderService.RunBatchRemindersAsync(
            storeId: User.GetStoreId() ?? 1,
            maxCount: body.MaxCount,
            userId: User.GetUserId(),
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    // ── 3. POST /api/reminders/auto ──────────────────────────────────

    [HttpPost("auto")]
    public async Task<IActionResult> RunAutomaticReminders(CancellationToken cancellationToken)
    {
        AutoReminderResult result = await _scheduler.RunNowAsync(cancellationToken);
        return Ok(result);
    }

    // ── 4. GET /api/reminders/scheduler/status ───────────────────────

    [HttpGet("scheduler/status")]
    public IActionResult GetSchedulerStatus()
    {
        return Ok(_scheduler.GetStatus());
    }

    // ── 5. POST /api/reminders/scheduler/start ───────────────────────

    [HttpPost("scheduler/start")]
    public IActionResult StartScheduler()
    {
        _scheduler.Start();
        return Ok(new { message = "Scheduler started" });
    }

    // ── 6. POST /api/reminders/scheduler/stop ────────────────────────

    [HttpPost("scheduler/stop")]
    public IActionResult StopScheduler()
    {
        _scheduler.Stop();
        return Ok(new { message = "Scheduler stopped" });
    }

    // ── 7. GET /api/reminders/history/ticket/{ticketKey} ─────────────

    [HttpGet("history/ticket/{ticketKey:int}")]
    public async Task<IActionResult> GetTicketHistory(int ticketKey, CancellationToken cancellationToken)
    {
        List<ReminderHistoryItem> history =
            await _reminderService.GetReminderHistoryAsync(ticketKey: ticketKey, cancellationToken: cancellationToken);
        return Ok(history);
    }

    // ── 8. GET /api/reminders/history/customer/{customerKey} ─────────

    [HttpGet("history/customer/{customerKey:int}")]
    public async Task<IActionResult> GetCustomerHistory(int customerKey, CancellationToken cancellationToken)
    {
        List<ReminderHistoryItem> history =
            await _reminderService.GetReminderHistoryAsync(customerKey: customerKey, cancellationToken: cancellationToken);
        return Ok(history);
    }

    // ── 9. GET /api/reminders/history/phone/{phone} ──────────────────

    [HttpGet("history/phone/{phone}")]
    public async Task<IActionResult> GetPhoneHistory(string phone, CancellationToken cancellationToken)
    {
        List<ReminderHistoryItem> history =
            await _reminderService.GetReminderHistoryAsync(phone: phone, cancellationToken: cancellationToken);
        return Ok(history);
    }

    // ── 10. GET /api/reminders/next/{ticketKey} ──────────────────────

    [HttpGet("next/{ticketKey:int}")]
    public async Task<IActionResult> GetNextReminder(
        int ticketKey,
        [FromQuery] string dueDate,
        [FromQuery] int daysLate,
        CancellationToken cancellationToken)
    {
        NextReminderResult? result =
            await _reminderService.GetNextReminderTypeAsync(ticketKey, dueDate, daysLate, cancellationToken);

        if (result is null)
            return Ok(new { message = "All applicable reminders have been sent" });

        return Ok(result);
    }

    // ── 11. GET /api/reminders/statistics ────────────────────────────

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        List<ReminderStatistic> stats = await _reminderService.GetStatisticsAsync(cancellationToken);
        return Ok(stats);
    }

    // ── 12. GET /api/reminders/recent ────────────────────────────────

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentReminders(
        [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        List<ReminderHistoryItem> recent =
            await _reminderService.GetRecentRemindersAsync(limit, cancellationToken);
        return Ok(recent);
    }

    // ── 13. GET /api/reminders/sent ──────────────────────────────────

    [HttpGet("sent")]
    public async Task<IActionResult> GetSentReminders(
        [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        List<SentReminderItem> sent =
            await _reminderService.GetSentRemindersAsync(limit, cancellationToken);
        return Ok(sent);
    }

    // ── 14. POST /api/reminders/exclude ──────────────────────────────

    [HttpPost("exclude")]
    public async Task<IActionResult> ExcludePhone(
        [FromBody] ExcludePhoneApiRequest body, CancellationToken cancellationToken)
    {
        bool success = await _reminderService.AddToExcludedAsync(
            body.Phone, body.Reason, User.GetUserId(), cancellationToken);

        if (success)
            return Ok(new { message = $"Phone {body.Phone} added to exclusion list" });

        return BadRequest(new { detail = "Failed to add phone to exclusion list" });
    }

    // ── 15. POST /api/reminders/unsubscribe ──────────────────────────

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> UnsubscribePhone(
        [FromBody] UnsubscribePhoneApiRequest body, CancellationToken cancellationToken)
    {
        bool success = await _reminderService.AddToUnsubscribedAsync(
            body.Phone, body.Method, body.Notes, cancellationToken);

        if (success)
            return Ok(new { message = $"Phone {body.Phone} unsubscribed" });

        return BadRequest(new { detail = "Failed to unsubscribe phone" });
    }

    // ── 16. GET /api/reminders/excluded/{phone} ──────────────────────

    [HttpGet("excluded/{phone}")]
    public async Task<IActionResult> CheckIfExcluded(string phone, CancellationToken cancellationToken)
    {
        bool isExcluded = await _reminderService.IsPhoneExcludedAsync(phone, cancellationToken);
        return Ok(new { phone, is_excluded = isExcluded });
    }

}

// ── API Request DTOs ────────────────────────────────────────────────

public sealed class SendReminderApiRequest
{
    public int TicketKey { get; set; }
    public int CustomerKey { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string TransNo { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public int DaysDiff { get; set; }
}

public sealed class BatchReminderApiRequest
{
    public int MaxCount { get; set; } = 100;
}

public sealed class ExcludePhoneApiRequest
{
    public string Phone { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class UnsubscribePhoneApiRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Method { get; set; } = "MANUAL";
    public string? Notes { get; set; }
}
