namespace SmsOpsHQ.Core.Services;

// Contract for the pawn reminder system.
// Manages sending, tracking, exclusions, and history for SMS reminders.
public interface IReminderService
{
    // Send a single reminder SMS for one ticket.
    Task<ReminderSendResult> SendReminderAsync(
        SendReminderRequest request, CancellationToken cancellationToken = default);

    // Send a combined SMS for multiple tickets belonging to the same customer.
    Task<ReminderSendResult> SendCombinedReminderAsync(
        SendCombinedReminderRequest request, CancellationToken cancellationToken = default);

    // Batch-process all eligible active tickets and send appropriate reminders.
    Task<BatchReminderResult> RunBatchRemindersAsync(
        int storeId, int maxCount, int? userId, CancellationToken cancellationToken = default);

    // Determine the next unsent reminder type for a ticket.
    Task<NextReminderResult?> GetNextReminderTypeAsync(
        int ticketKey, string dueDate, int daysLate, CancellationToken cancellationToken = default);

    // Query reminder history by ticket, customer, or phone.
    Task<List<ReminderHistoryItem>> GetReminderHistoryAsync(
        int? ticketKey = null, int? customerKey = null, string? phone = null,
        int limit = 50, CancellationToken cancellationToken = default);

    // Get aggregate statistics grouped by reminder type.
    Task<List<ReminderStatistic>> GetStatisticsAsync(CancellationToken cancellationToken = default);

    // Get the most recent reminders across all tickets.
    Task<List<ReminderHistoryItem>> GetRecentRemindersAsync(
        int limit = 20, CancellationToken cancellationToken = default);

    // Get sent reminders formatted for inbox display with customer info.
    Task<List<SentReminderItem>> GetSentRemindersAsync(
        int limit = 100, CancellationToken cancellationToken = default);

    // Check if a phone is excluded or unsubscribed.
    Task<bool> IsPhoneExcludedAsync(string phone, CancellationToken cancellationToken = default);

    // Add a phone to the exclusion list.
    Task<bool> AddToExcludedAsync(
        string phone, string? reason, int? userId, CancellationToken cancellationToken = default);

    // Add a phone to the unsubscribed list.
    Task<bool> AddToUnsubscribedAsync(
        string phone, string method = "MANUAL", string? notes = null,
        CancellationToken cancellationToken = default);
}

// ── Request DTOs ────────────────────────────────────────────────────

public sealed class SendReminderRequest
{
    public int TicketKey { get; set; }
    public int CustomerKey { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string TransNo { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public int DaysDiff { get; set; }
    public int? UserId { get; set; }
    public int StoreId { get; set; } = 1;
}

public sealed class SendCombinedReminderRequest
{
    public int CustomerKey { get; set; }
    public string Phone { get; set; } = string.Empty;
    public List<CombinedTicketInfo> Tickets { get; set; } = new();
    public int? UserId { get; set; }
    public int StoreId { get; set; } = 1;
}

public sealed class CombinedTicketInfo
{
    public int TicketKey { get; set; }
    public string TransNo { get; set; } = string.Empty;
    public string DueDateStr { get; set; } = string.Empty;
    public int DaysLate { get; set; }
    public NextReminderResult NextReminder { get; set; } = new();
}

// ── Result DTOs ─────────────────────────────────────────────────────

public sealed class ReminderSendResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TwilioSid { get; set; }
    public string? ReminderType { get; set; }
    public bool TestMode { get; set; }
    public bool DriftDetected { get; set; }
    public int TicketCount { get; set; } = 1;
}

public sealed class BatchReminderResult
{
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int CombinedCount { get; set; }
    public List<BatchReminderDetail> Details { get; set; } = new();
}

public sealed class BatchReminderDetail
{
    public int? TicketKey { get; set; }
    public List<int>? TicketKeys { get; set; }
    public string? TransNo { get; set; }
    public List<string>? TransNos { get; set; }
    public string? Phone { get; set; }
    public string? ReminderType { get; set; }
    public int TicketCount { get; set; } = 1;
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class NextReminderResult
{
    public string ReminderType { get; set; } = string.Empty;
    public int DaysDiff { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ReminderHistoryItem
{
    public int ReminderId { get; set; }
    public int? TicketKey { get; set; }
    public int? CustomerKey { get; set; }
    public string? DueDate { get; set; }
    public string? Phone { get; set; }
    public string? ReminderType { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TwilioSid { get; set; }
    public string? Error { get; set; }
    public DateTime? SentAt { get; set; }
    public int? SentByUserId { get; set; }
    public int? StoreId { get; set; }
}

public sealed class ReminderStatistic
{
    public string ReminderType { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public double SuccessRate { get; set; }
}

public sealed class SentReminderItem
{
    public int ReminderId { get; set; }
    public int? TicketKey { get; set; }
    public int? CustomerKey { get; set; }
    public string? DueDate { get; set; }
    public string? Phone { get; set; }
    public string? ReminderType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime? SentAt { get; set; }
    public string? TwilioSid { get; set; }
    public string CustomerName { get; set; } = "Unknown";
    public int? TransNo { get; set; }
}
