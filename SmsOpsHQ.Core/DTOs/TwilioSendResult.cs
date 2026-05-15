namespace SmsOpsHQ.Core.DTOs;

// Result from ITwilioService.SendSmsAsync.
public sealed class TwilioSendResult
{
    public bool Success { get; set; }
    public string? TwilioSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True when the message was not actually delivered to Twilio because
    /// the service is running in mock mode (AccountSid/AuthToken not configured).
    /// Callers MUST surface this to the user — the message did not reach the carrier.
    /// </summary>
    public bool IsMock { get; set; }
}
