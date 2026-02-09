namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the AuditLog table.
public sealed class AuditLogEntity
{
    public int AuditId { get; set; }
    public int? UserId { get; set; }
    public int? StoreId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IPAddress { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public UserEntity? User { get; set; }
    public StoreEntity? Store { get; set; }
}
