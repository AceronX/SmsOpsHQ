namespace SmsOpsHQ.Core.DTOs;

// Message data returned in API responses.
public sealed class MessageDto
{
    public int MessageId { get; set; }
    public int ThreadId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FromE164 { get; set; } = string.Empty;
    public string ToE164 { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? MediaJson { get; set; }
    public string Category { get; set; } = "general";
    public string Status { get; set; } = string.Empty;
    public string? TwilioSid { get; set; }
    public int? SentByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
