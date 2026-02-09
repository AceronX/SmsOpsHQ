namespace SmsOpsHQ.Core.Entities;

// Quarantined inbound message flagged as suspicious or spam.
// Maps to the QuarantinedMessages table.
public sealed class QuarantinedMessage
{
    public int QuarantineId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // Sender phone in E.164 format
    public string FromE164 { get; set; } = string.Empty;

    // Recipient phone in E.164 format
    public string ToE164 { get; set; } = string.Empty;

    // Message body text
    public string? Body { get; set; }

    // JSON array of media URLs
    public string? MediaJson { get; set; }

    // Twilio message SID
    public string? TwilioSid { get; set; }

    // Why this message was quarantined
    public string? QuarantineReason { get; set; }

    // When the message was quarantined (UTC)
    public DateTime QuarantinedAt { get; set; } = DateTime.UtcNow;

    // When a reviewer resolved this message (UTC), null if pending
    public DateTime? ReviewedAt { get; set; }

    // FK to Users.UserId — who reviewed this message
    public int? ReviewedByUserId { get; set; }

    // "Approved" | "Rejected" | "Spam", null if pending
    public string? Resolution { get; set; }
}
