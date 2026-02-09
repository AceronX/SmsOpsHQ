namespace SmsOpsHQ.Core.Entities;

// SMS message record. Maps to the Messages table.
public sealed class Message
{
    public int MessageId { get; set; }

    // FK to Threads.ThreadId
    public int ThreadId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // Store's Twilio number in E.164 format
    public string? StorePhone { get; set; }

    // "Inbound" | "Outbound" | "Note"
    public string Direction { get; set; } = string.Empty;

    // Sender phone in E.164 format
    public string FromE164 { get; set; } = string.Empty;

    // Recipient phone in E.164 format
    public string ToE164 { get; set; } = string.Empty;

    // Message text content
    public string? Body { get; set; }

    // JSON array of media URLs
    public string? MediaJson { get; set; }

    // Auto-classified: "general" | "reminder" | "directions" | "promotions"
    public string Category { get; set; } = "general";

    // Delivery status: "Queued" | "Sent" | "Delivered" | "Failed" | "Undelivered" | "Received" | "Internal"
    public string Status { get; set; } = string.Empty;

    // Twilio message SID for delivery tracking (null for notes)
    public string? TwilioSid { get; set; }

    // FK to Users.UserId — who sent this message
    public int? SentByUserId { get; set; }

    // Twilio error code on failure
    public string? ErrorCode { get; set; }

    // Twilio error description on failure
    public string? ErrorText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
