namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.RelayMediaItem. Used inside
// TwilioInboundRelayPayload for MMS attachments.
public sealed class RelayMediaItem
{
    public int Index { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ContentType { get; set; }
}
