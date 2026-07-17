using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Hubs;

namespace SmsOpsHQ.Infrastructure.Services;

// Pushes real-time updates to connected clients via SignalR hub.
// Each store has a group; broadcasts are scoped to the store group.
public sealed class RealtimeService : IRealtimeService
{
    private readonly IHubContext<SmsOpsHub> _hubContext;
    private readonly ILogger<RealtimeService> _logger;

    public RealtimeService(IHubContext<SmsOpsHub> hubContext, ILogger<RealtimeService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PushMessageNewAsync(int storeId, int threadId,
        MessageDto message, ThreadDto thread,
        CancellationToken cancellationToken = default)
    {
        string group = SmsOpsHub.StoreGroupName(storeId);

        // Client registers On<JsonElement, JsonElement>("MessageNew") — must send exactly 2 args.
        // Include threadId at top level so the client can filter by thread.
        object messagePayload = new { storeId, threadId, message.MessageId, message.Direction,
            message.FromE164, message.ToE164, message.Body, message.Status,
            message.TwilioSid, message.MediaJson, message.Category, message.CreatedAt };
        object threadPayload = new { thread.ThreadId, thread.StoreId, thread.Status,
            thread.TwilioNumberId, thread.ContactPhoneE164,
            thread.LastMessageAt, thread.UnreadCount };

        await _hubContext.Clients.Group(group).SendAsync(
            "MessageNew", messagePayload, threadPayload, cancellationToken);

        _logger.LogDebug("Pushed MessageNew to group {Group} for thread {ThreadId}",
            group, threadId);
    }

    public async Task PushMessageStatusAsync(int storeId, int threadId,
        int messageId, string twilioSid, string status, string? errorCode,
        CancellationToken cancellationToken = default)
    {
        string group = SmsOpsHub.StoreGroupName(storeId);

        // Client registers On<int, int, string, string?>("MessageStatus")
        await _hubContext.Clients.Group(group).SendAsync(
            "MessageStatus", storeId, messageId, status, errorCode ?? "", cancellationToken);

        _logger.LogDebug("Pushed MessageStatus to group {Group}: SID={Sid} Status={Status}",
            group, twilioSid, status);
    }

    public async Task PushSystemAlertAsync(int storeId, string code,
        string message, string severity,
        CancellationToken cancellationToken = default)
    {
        string group = SmsOpsHub.StoreGroupName(storeId);

        // Client registers On<string, string, string>("SystemAlert")
        await _hubContext.Clients.Group(group).SendAsync(
            "SystemAlert", code, message, severity, cancellationToken);

        _logger.LogInformation("Pushed SystemAlert to group {Group}: {Code} [{Severity}]",
            group, code, severity);
    }
}
