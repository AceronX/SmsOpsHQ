namespace SmsOpsHQ.Core.Entities;

// Conversation thread grouping messages. Maps to the Threads table.
public sealed class Thread
{
    public int ThreadId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // DEPRECATED: Use IdentityId. FK to Customers.CustomerId.
    public int? CustomerId { get; set; }

    // Store-side number for this conversation. FK to TwilioNumbers.NumberId.
    public int? TwilioNumberId { get; set; }

    // Customer-side phone that identifies this conversation. Null is reserved
    // for legacy rows that could not be resolved during migration.
    public string? ContactPhoneE164 { get; set; }

    // Canonical customer identity key (nullable during migration)
    public int? IdentityId { get; set; }

    // "Open" | "Closed" | "Archived"
    public string Status { get; set; } = "Open";

    // FK to Users.UserId — assigned staff member
    public int? AssignedToUserId { get; set; }

    // Timestamp of the most recent message in this thread (UTC)
    public DateTime? LastMessageAt { get; set; }

    // Number of unread inbound messages
    public int UnreadCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
