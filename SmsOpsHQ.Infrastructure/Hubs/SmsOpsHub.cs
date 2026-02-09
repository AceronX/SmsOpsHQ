using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace SmsOpsHQ.Infrastructure.Hubs;

// SignalR hub for real-time SMS operations updates.
// Clients join store-scoped groups to receive broadcasts.
// HQ users may join the "hq" group to receive all-store broadcasts.
public sealed class SmsOpsHub : Hub
{
    private readonly ILogger<SmsOpsHub> _logger;

    public SmsOpsHub(ILogger<SmsOpsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Client calls this to join a store group and receive that store's broadcasts.
    public async Task JoinStoreGroup(int storeId)
    {
        string groupName = StoreGroupName(storeId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} joined group {Group}",
            Context.ConnectionId, groupName);
    }

    // Client calls this to leave a store group.
    public async Task LeaveStoreGroup(int storeId)
    {
        string groupName = StoreGroupName(storeId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} left group {Group}",
            Context.ConnectionId, groupName);
    }

    // Builds the group name for a given store.
    public static string StoreGroupName(int storeId) => $"store_{storeId}";
}
