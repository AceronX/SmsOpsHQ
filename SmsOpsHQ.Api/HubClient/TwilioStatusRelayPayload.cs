namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.TwilioStatusRelayPayload. Received over
// SignalR (method HubConstants.AgentMethods.DeliverMessageStatus) when HQ
// forwards a Twilio delivery-status callback for a message this store sent.
public sealed class TwilioStatusRelayPayload
{
    public string DeploymentId { get; set; } = string.Empty;
    public string MessageSid { get; set; } = string.Empty;
    public string MessageStatus { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
