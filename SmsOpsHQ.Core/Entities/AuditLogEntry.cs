namespace SmsOpsHQ.Core.Entities;

// Audit trail entry. Maps to the AuditLog table.
public sealed class AuditLogEntry
{
    public int AuditId { get; set; }

    // FK to Users.UserId — who performed the action
    public int? UserId { get; set; }

    // FK to Stores.StoreId — which store context
    public int? StoreId { get; set; }

    // Action performed, e.g. "SendMessage", "DeleteThread", "Login"
    public string Action { get; set; } = string.Empty;

    // Type of entity affected, e.g. "Message", "Thread", "Customer"
    public string? EntityType { get; set; }

    // Primary key of the affected entity
    public int? EntityId { get; set; }

    // JSON details of the action
    public string? Details { get; set; }

    // IP address of the requester
    public string? IPAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
