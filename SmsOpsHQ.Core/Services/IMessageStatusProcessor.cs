using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

/// <summary>
/// Applies a Twilio delivery-status callback to the local message row and
/// emits the realtime push. Same logic regardless of how the callback arrived
/// (HTTP webhook vs SignalR relay from the Hub).
/// </summary>
public interface IMessageStatusProcessor
{
    Task<MessageStatusProcessingResult> ProcessAsync(MessageStatusUpdate update, CancellationToken cancellationToken = default);
}
