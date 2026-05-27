namespace SmsOpsHQ.Core.DTOs;

/// <summary>
/// Normalized inbound-SMS request that drives <c>IInboundSmsProcessor</c>.
/// Same shape regardless of whether the message arrived via HTTP webhook
/// (<c>TwilioInboundController</c>) or via SignalR relay from <c>SmsOpsHQ.Hub</c>.
/// </summary>
public sealed class InboundSmsRequest
{
    /// <summary>Twilio's globally unique message id; used for idempotency.</summary>
    public string MessageSid { get; set; } = string.Empty;

    /// <summary>Customer phone (E.164), the sender.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Store's Twilio phone (E.164), the recipient.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Plain-text message body. May be empty for MMS-only messages.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Number of media attachments included in <see cref="Media"/>.</summary>
    public int NumMedia { get; set; }

    /// <summary>Parsed media attachments; empty when <see cref="NumMedia"/> is 0.</summary>
    public List<InboundMediaItem> Media { get; set; } = new();

    /// <summary>UTC timestamp captured by the original receiver (Twilio webhook
    /// or Hub). Falls back to "now" when missing.</summary>
    public DateTime? ReceivedAtUtc { get; set; }
}

/// <summary>One media attachment on an inbound MMS.</summary>
public sealed class InboundMediaItem
{
    public int Index { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ContentType { get; set; }
}
