namespace SmsOpsHQ.Core.Entities;

// Opt-out record for SMS compliance. Maps to the OptOuts table.
// A phone opted out at a store must never receive outbound SMS from that store.
public sealed class OptOut
{
    public int OptOutId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // Phone number that opted out, in E.164 format
    public string PhoneE164 { get; set; } = string.Empty;

    // When the opt-out was recorded (UTC)
    public DateTime OptOutDate { get; set; } = DateTime.UtcNow;

    // Free-text reason for opt-out
    public string? Reason { get; set; }
}
