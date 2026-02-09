namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Threads table.
public sealed class ThreadEntity
{
    public int ThreadId { get; set; }
    public int StoreId { get; set; }
    public int? CustomerId { get; set; }
    public int? TwilioNumberId { get; set; }
    public int? IdentityId { get; set; }
    public string Status { get; set; } = "Open";
    public int? AssignedToUserId { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
    public UserEntity? AssignedToUser { get; set; }
    public List<MessageEntity> Messages { get; set; } = new();
}
