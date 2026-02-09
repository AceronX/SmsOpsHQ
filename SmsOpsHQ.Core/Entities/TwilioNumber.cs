namespace SmsOpsHQ.Core.Entities;

// Twilio phone number provisioned for SMS. Maps to the TwilioNumbers table.
public sealed class TwilioNumber
{
    public int NumberId { get; set; }

    // FK to Stores.StoreId -- which store owns this number
    public int StoreId { get; set; }

    // E.164 format, e.g. "+19294990435"
    public string PhoneE164 { get; set; } = string.Empty;

    // Human-readable label
    public string? FriendlyName { get; set; }

    // Twilio resource SID, e.g. "PNxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
    public string? TwilioSid { get; set; }

    // Twilio Messaging Service SID for this number
    public string? MessagingServiceSid { get; set; }

    // Raw JSON capabilities, e.g. {"sms":true,"mms":true,"voice":true}
    public string? CapabilitiesJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
