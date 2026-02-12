namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Users table.
public sealed class UserEntity
{
    public int UserId { get; set; }
    public int? StoreId { get; set; }
    public int? TwilioNumberId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public StoreEntity? Store { get; set; }
}
