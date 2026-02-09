namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Stores table.
public sealed class StoreEntity
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Phone { get; set; }
    public int? DefaultNumberId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<UserEntity> Users { get; set; } = new();
    public List<TwilioNumberEntity> TwilioNumbers { get; set; } = new();
}
