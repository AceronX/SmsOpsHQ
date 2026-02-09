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
        await _hubContext.Clients.Group(group).SendAsync(
            "MessageNew",
            new { storeId, threadId, message, thread },
            cancellationToken);

        _logger.LogDebug("Pushed MessageNew to group {Group} for thread {ThreadId}",
            group, threadId);
    }

    public async Task PushMessageStatusAsync(int storeId, int threadId,
        int messageId, string twilioSid, string status, string? errorCode,
        CancellationToken cancellationToken = default)
    {
        string group = SmsOpsHub.StoreGroupName(storeId);
        await _hubContext.Clients.Group(group).SendAsync(
            "MessageStatus",
            new { storeId, threadId, messageId, twilioSid, status, errorCode },
            cancellationToken);

        _logger.LogDebug("Pushed MessageStatus to group {Group}: SID={Sid} Status={Status}",
            group, twilioSid, status);
    }

    public async Task PushSystemAlertAsync(int storeId, string code,
        string message, string severity,
        CancellationToken cancellationToken = default)
    {
        string group = SmsOpsHub.StoreGroupName(storeId);
        await _hubContext.Clients.Group(group).SendAsync(
            "SystemAlert",
            new { storeId, code, message, severity },
            cancellationToken);

        _logger.LogInformation("Pushed SystemAlert to group {Group}: {Code} [{Severity}]",
            group, code, severity);
    }
}
