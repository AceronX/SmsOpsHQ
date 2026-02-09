using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for pushing real-time updates to connected clients via SignalR.
public interface IRealtimeService
{
    // Broadcast a new message to all clients in the store group.
    Task PushMessageNewAsync(int storeId, int threadId,
        MessageDto message, ThreadDto thread,
        CancellationToken cancellationToken = default);

    // Broadcast a message delivery status update to the store group.
    Task PushMessageStatusAsync(int storeId, int threadId,
        int messageId, string twilioSid, string status, string? errorCode,
        CancellationToken cancellationToken = default);

    // Broadcast a system alert to the store group.
    Task PushSystemAlertAsync(int storeId, string code,
        string message, string severity,
        CancellationToken cancellationToken = default);
}
