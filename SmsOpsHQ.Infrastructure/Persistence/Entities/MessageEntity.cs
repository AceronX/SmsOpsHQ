namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Messages table.
public sealed class MessageEntity
{
    public int MessageId { get; set; }
    public int ThreadId { get; set; }
    public int StoreId { get; set; }
    public string? StorePhone { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FromE164 { get; set; } = string.Empty;
    public string ToE164 { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? MediaJson { get; set; }
    public string Category { get; set; } = "general";
    public string Status { get; set; } = string.Empty;
    public string? TwilioSid { get; set; }
    public int? SentByUserId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public ThreadEntity Thread { get; set; } = null!;
    public StoreEntity Store { get; set; } = null!;
    public UserEntity? SentByUser { get; set; }
}
