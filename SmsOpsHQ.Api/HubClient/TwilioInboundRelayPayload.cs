namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.TwilioInboundRelayPayload. Received over
// SignalR (method HubConstants.AgentMethods.DeliverInboundSms) when HQ forwards
// an inbound Twilio SMS that belongs to this store. The store re-validates
// DeploymentId and then runs its standard inbound pipeline against this DTO.
public sealed class TwilioInboundRelayPayload
{
    public string DeploymentId { get; set; } = string.Empty;
    public string MessageSid { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int NumMedia { get; set; }
    public List<RelayMediaItem> Media { get; set; } = new();
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
