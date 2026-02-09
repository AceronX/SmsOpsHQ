namespace SmsOpsHQ.Core.Entities;

// Local cache of customer data. Maps to the Customers table.
public sealed class Customer
{
    public int CustomerId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // Primary contact phone in E.164 format
    public string PhoneE164 { get; set; } = string.Empty;

    // XPD primary key linking to XPD_Customers.Key
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
}
