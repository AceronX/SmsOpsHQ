namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Customers table.
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

    // Navigation
    public StoreEntity Store { get; set; } = null!;
}
