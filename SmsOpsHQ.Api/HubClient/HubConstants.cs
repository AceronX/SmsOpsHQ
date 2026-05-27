namespace SmsOpsHQ.Api.HubClient;

// Mirror of SmsOpsHQ.Hub.Contracts.HubConstants -- copied here so the store
// project doesn't take a code dependency on the Hub repo. As long as the
// constants stay in sync, JSON-over-HTTP and SignalR both work either way.
//
// If you change a value here, change it in SmsOpsHQ.Hub.Contracts/HubConstants.cs
// as well -- they're a pact between the two repos.
public static class HubConstants
{
    public const string StoreKeyHeader = "X-Store-Key";

    public const string AgentHubPath = "/hubs/agent";

    public static class AgentMethods
    {
        // Store -> HQ
        public const string ReceiveHeartbeat = nameof(ReceiveHeartbeat);

        // HQ -> Store
        public const string RunXpdSyncNow = nameof(RunXpdSyncNow);
        public const string RequestImmediateHeartbeat = nameof(RequestImmediateHeartbeat);

        // HQ -> Store: central Twilio webhook relay (central-webhook design).
        // Payload types: TwilioInboundRelayPayload / TwilioStatusRelayPayload.
        public const string DeliverInboundSms = nameof(DeliverInboundSms);
        public const string DeliverMessageStatus = nameof(DeliverMessageStatus);
    }
}
