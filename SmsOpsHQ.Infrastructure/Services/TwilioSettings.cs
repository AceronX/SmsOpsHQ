namespace SmsOpsHQ.Infrastructure.Services;

// Configuration values for Twilio SMS, bound from appsettings "Twilio" section.
// When AccountSid or AuthToken are empty, the service operates in mock mode.
public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}
