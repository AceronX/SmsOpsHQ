namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the QuarantinedMessages table.
public sealed class QuarantinedMessageEntity
{
    public int QuarantineId { get; set; }
    public int StoreId { get; set; }
    public string FromE164 { get; set; } = string.Empty;
    public string ToE164 { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? MediaJson { get; set; }
    public string? TwilioSid { get; set; }
    public string? QuarantineReason { get; set; }
    public DateTime QuarantinedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? Resolution { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
    public UserEntity? ReviewedByUser { get; set; }
}
