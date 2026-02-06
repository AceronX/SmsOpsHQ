namespace SmsOpsHQ.Core.Entities;

// System user (store staff or HQ personnel). Maps to the Users table.
public sealed class User
{
    public int UserId { get; set; }

    // FK to Stores. Null for HQ users who can access all stores.
    public int? StoreId { get; set; }

    // Display name, e.g. "Jane Doe"
    public string FullName { get; set; } = string.Empty;

    // Unique login name
    public string Username { get; set; } = string.Empty;

    // BCrypt hashed password
    public string PasswordHash { get; set; } = string.Empty;

    // RBAC role: HQAdmin | HQViewer | StoreAdmin | StoreManager
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Last successful login time (UTC). Null if never logged in.
    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
