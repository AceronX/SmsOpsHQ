namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Customers table.
// Contains both SMS-originated fields and XPawn-synced pawn data.
public sealed class CustomerEntity
{
    public int CustomerId { get; set; }
    public int StoreId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
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
    public DateTime? SinceDate { get; set; }
    public string? TagsJson { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // XPawn-synced columns (null for SMS-only customers)
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

    // Navigation
    public StoreEntity Store { get; set; } = null!;
}
