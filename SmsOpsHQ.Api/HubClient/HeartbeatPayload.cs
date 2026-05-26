namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.HeartbeatPayload. Keep the field names
// exactly the same; serializer uses camelCase on the wire (configured in
// HeartbeatPusher's HttpClient).
public sealed class HeartbeatPayload
{
    public string DeploymentId { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public TimeSpan Uptime { get; set; }

    public string TwilioMode { get; set; } = "unknown";
    public bool TwilioMock { get; set; }

    public DateTime? XpdLastSyncUtc { get; set; }
    public bool XpdLastSyncSuccess { get; set; }
    public string? XpdLastSyncError { get; set; }
    public bool XpdSchedulerRunning { get; set; }
    public DateTime? XpdSchedulerNextRunUtc { get; set; }

    public int MessagesSentToday { get; set; }
    public int MessagesReceivedToday { get; set; }
    public int UnreadCount { get; set; }
    public int CustomerCount { get; set; }
    public int ActiveTicketCount { get; set; }

    public DateTime? LastUserActivityUtc { get; set; }
    public int OnlineUserCount { get; set; }
}
