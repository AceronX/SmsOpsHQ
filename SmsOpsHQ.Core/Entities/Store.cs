namespace SmsOpsHQ.Core.Entities;

// Store location. Maps to the Stores table.
public sealed class Store
{
    public int StoreId { get; set; }

    // Display name, e.g. "Pitkin Ave"
    public string StoreName { get; set; } = string.Empty;

    public string? Address { get; set; }

    public string? City { get; set; }

    // US state abbreviation, e.g. "NY"
    public string? State { get; set; }

    public string? Zip { get; set; }

    // FK to TwilioNumbers. Default outbound number for this store.
    // 0 means no Twilio number set yet.
    public int DefaultNumberId { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
