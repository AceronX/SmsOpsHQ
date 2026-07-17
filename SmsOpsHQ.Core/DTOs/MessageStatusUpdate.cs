namespace SmsOpsHQ.Core.DTOs;

/// <summary>
/// Normalized delivery-status callback that drives <c>IMessageStatusProcessor</c>.
/// Same shape for HTTP webhook (<c>TwilioStatusController</c>) and SignalR relay
/// from <c>SmsOpsHQ.Hub</c>.
/// </summary>
public sealed class MessageStatusUpdate
{
    public string MessageSid { get; set; } = string.Empty;

    /// <summary>Twilio status verb -- "queued", "sent", "delivered", "failed", etc.
    /// Lowercase as Twilio sends it; the processor normalizes capitalization.</summary>
    public string MessageStatus { get; set; } = string.Empty;

    /// <summary>Optional Twilio numeric error code on failed/undelivered.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Optional provider error description when supplied by the ingress.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC timestamp captured by the original receiver (Twilio webhook
    /// or Hub). Falls back to "now" when missing.</summary>
    public DateTime? ReceivedAtUtc { get; set; }
}
