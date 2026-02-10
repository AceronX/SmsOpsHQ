namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity for the CustomerPhones table (phone index).
// Used by IdentityResolver for phone-to-CustomerKey lookups.
// Rebuilt during sync.
public sealed class CustomerPhoneEntity
{
    public int CustomerKey { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? PhoneOriginal { get; set; }
    public string PhoneType { get; set; } = string.Empty;
}
