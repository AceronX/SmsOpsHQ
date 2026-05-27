namespace SmsOpsHQ.Core.DTOs;

/// <summary>
/// Terminal outcome of <c>IInboundSmsProcessor.ProcessAsync</c>. Callers use
/// this for audit/logging only; the HTTP and SignalR transports always
/// acknowledge the receive so Twilio (and the Hub) don't retry-storm.
/// </summary>
public enum InboundSmsResultKind
{
    /// <summary>New message recorded and a realtime push was emitted.</summary>
    Processed = 1,

    /// <summary>A message with the same Twilio SID already exists; safe no-op.</summary>
    Duplicate = 2,

    /// <summary>Neither the <c>To</c> nor <c>From</c> number maps to a known store.</summary>
    NoStoreMatch = 3,

    /// <summary>Validation failed AND the message was written to the quarantine table.</summary>
    Quarantined = 4,

    /// <summary>Validation failed but not severe enough to quarantine (e.g. echo/loopback).</summary>
    Rejected = 5,

    /// <summary>Unexpected internal error during processing.</summary>
    Error = 6,
}

public sealed record InboundSmsProcessingResult(
    InboundSmsResultKind Kind,
    int? StoreId,
    int? ThreadId,
    int? MessageId,
    string? Reason = null);
