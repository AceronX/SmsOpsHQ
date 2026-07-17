namespace SmsOpsHQ.Core.DTOs;

// Thread data returned in API responses (e.g. inbox list).
public sealed class ThreadDto
{
    public int ThreadId { get; set; }
    public int StoreId { get; set; }
    public int? IdentityId { get; set; }
    public int? TwilioNumberId { get; set; }
    public string? ContactPhoneE164 { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }

    // Most recent message in this thread (populated for inbox view)
    public MessageDto? LastMessage { get; set; }

    // Customer associated with this thread (populated for inbox view)
    public CustomerDto? Customer { get; set; }
}
