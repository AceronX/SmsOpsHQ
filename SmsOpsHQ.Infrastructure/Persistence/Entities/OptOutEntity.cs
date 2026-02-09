namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the OptOuts table.
public sealed class OptOutEntity
{
    public int OptOutId { get; set; }
    public int StoreId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public DateTime OptOutDate { get; set; }
    public string? Reason { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
}
