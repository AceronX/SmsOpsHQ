namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.StorePhoneSnapshot. Sent inside
// HeartbeatPayload so HQ keeps its phone -> store routing in sync without
// manual entry. Keep the field names exactly the same as the Contracts copy.
public sealed class StorePhoneSnapshot
{
    public string PhoneE164 { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
