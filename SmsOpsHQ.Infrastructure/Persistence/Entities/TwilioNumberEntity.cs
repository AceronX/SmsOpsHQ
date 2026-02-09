namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the TwilioNumbers table.
public sealed class TwilioNumberEntity
{
    public int NumberId { get; set; }
    public int StoreId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public string? TwilioSid { get; set; }
    public string? MessagingServiceSid { get; set; }
    public string? CapabilitiesJson { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
}
