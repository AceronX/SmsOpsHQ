namespace SmsOpsHQ.Core.DTOs;

public enum MessageStatusResultKind
{
    /// <summary>DB row updated and realtime push emitted.</summary>
    Updated = 1,

    /// <summary>The Twilio SID isn't in our DB (status for a message we never owned).</summary>
    NotFound = 2,

    /// <summary>Payload had no MessageSid; nothing to do.</summary>
    Empty = 3,

    /// <summary>Unexpected internal error.</summary>
    Error = 4,
}

public sealed record MessageStatusProcessingResult(
    MessageStatusResultKind Kind,
    int? StoreId,
    int? ThreadId,
    int? MessageId,
    string? Reason = null);
