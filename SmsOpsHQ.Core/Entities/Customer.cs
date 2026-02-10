namespace SmsOpsHQ.Core.Entities;

// Unified customer data. Maps to the Customers table.
// Contains both SMS-originated fields and XPawn-synced pawn data.
public sealed class Customer
{
    public int CustomerId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // Primary contact phone in E.164 format
    public string PhoneE164 { get; set; } = string.Empty;

    // XPawn primary key (unique per customer in XPawn system)
    public int? CustomerKey { get; set; }

    public string? CellPhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }

    // Date customer first appeared in the system
    public DateTime? SinceDate { get; set; }

    // JSON array of customer tags
    public string? TagsJson { get; set; }

    // Free-text notes
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── XPawn-synced fields (null for SMS-only customers) ──────────────
    public string? MiddleName { get; set; }
    public string? ResPhone { get; set; }
    public string? BusPhone { get; set; }
    public string? EMailAddress { get; set; }
    public string? DOB { get; set; }
    public string? SSN { get; set; }
    public string? IDNo { get; set; }
    public string? IDIssueState { get; set; }
    public string? FirstTransaction { get; set; }
    public string? LastTransaction { get; set; }
    public string? Warning { get; set; }
    public string? SyncedAt { get; set; }
}
