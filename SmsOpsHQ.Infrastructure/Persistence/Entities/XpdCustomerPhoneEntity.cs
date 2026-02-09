namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the XPD_CustomerPhones table (phone index).
// Used by IdentityResolver for phone-to-CustomerKey lookups.
// The full table is rebuilt during XPD sync (Phase 3).
public sealed class XpdCustomerPhoneEntity
{
    public int CustomerKey { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? PhoneOriginal { get; set; }
    public string PhoneType { get; set; } = string.Empty;
}
