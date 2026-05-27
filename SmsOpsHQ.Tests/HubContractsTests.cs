using System.Text.Json;
using SmsOpsHQ.Api.HubClient;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Locks down the wire shape of the store-side mirror of Hub.Contracts.
/// These types travel JSON-over-SignalR (and JSON-over-HTTP for heartbeats),
/// so renames or missing fields would silently break field deployments.
///
/// Phase 1 of the central Twilio webhook design: just contracts + constants.
/// </summary>
public class HubContractsTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ---------------------------------------------------------------------
    // Constants: HQ -> Store SignalR method names
    // ---------------------------------------------------------------------

    [Fact]
    public void AgentMethods_DeliverInboundSms_HasExpectedValue()
    {
        Assert.Equal("DeliverInboundSms", HubConstants.AgentMethods.DeliverInboundSms);
    }

    [Fact]
    public void AgentMethods_DeliverMessageStatus_HasExpectedValue()
    {
        Assert.Equal("DeliverMessageStatus", HubConstants.AgentMethods.DeliverMessageStatus);
    }

    [Fact]
    public void AgentMethods_PreExistingValuesUnchanged()
    {
        // Guard against accidental rename. These are part of the existing
        // store<->Hub contract; changing them silently would break the field.
        Assert.Equal("ReceiveHeartbeat", HubConstants.AgentMethods.ReceiveHeartbeat);
        Assert.Equal("RunXpdSyncNow", HubConstants.AgentMethods.RunXpdSyncNow);
        Assert.Equal("RequestImmediateHeartbeat", HubConstants.AgentMethods.RequestImmediateHeartbeat);
    }

    // ---------------------------------------------------------------------
    // HeartbeatPayload.Phones (additive, must default to empty list)
    // ---------------------------------------------------------------------

    [Fact]
    public void HeartbeatPayload_Phones_DefaultsToEmptyList()
    {
        HeartbeatPayload payload = new();

        Assert.NotNull(payload.Phones);
        Assert.Empty(payload.Phones);
    }

    [Fact]
    public void HeartbeatPayload_WithPhones_RoundTripsThroughJson()
    {
        HeartbeatPayload payload = new()
        {
            DeploymentId = "depA",
            StoreName = "Dallas Pawn",
            Phones =
            {
                new StorePhoneSnapshot { PhoneE164 = "+15551110000", IsDefault = true },
                new StorePhoneSnapshot { PhoneE164 = "+15552220000", IsDefault = false },
            }
        };

        string json = JsonSerializer.Serialize(payload, CamelCase);
        HeartbeatPayload? decoded = JsonSerializer.Deserialize<HeartbeatPayload>(json, CamelCase);

        Assert.NotNull(decoded);
        Assert.Equal("depA", decoded!.DeploymentId);
        Assert.Equal(2, decoded.Phones.Count);
        Assert.Equal("+15551110000", decoded.Phones[0].PhoneE164);
        Assert.True(decoded.Phones[0].IsDefault);
        Assert.Equal("+15552220000", decoded.Phones[1].PhoneE164);
        Assert.False(decoded.Phones[1].IsDefault);
    }

    [Fact]
    public void HeartbeatPayload_OldClientPayloadWithoutPhones_DeserializesToEmptyList()
    {
        // Simulates a heartbeat from a pre-Phase-1 store build (no "phones" field).
        // The new field must tolerate older clients -- Phones should be either null
        // or an empty list, never throw.
        const string legacyJson = """
        {
          "deploymentId": "old-deployment",
          "storeName": "Legacy Shop",
          "appVersion": "0.9.0",
          "twilioMode": "live",
          "twilioMock": false,
          "messagesSentToday": 1,
          "messagesReceivedToday": 2,
          "unreadCount": 0,
          "customerCount": 100,
          "activeTicketCount": 5,
          "onlineUserCount": 0
        }
        """;

        HeartbeatPayload? decoded = JsonSerializer.Deserialize<HeartbeatPayload>(legacyJson, CamelCase);

        Assert.NotNull(decoded);
        Assert.Equal("old-deployment", decoded!.DeploymentId);
        // Phones is set by the property initializer; missing JSON leaves it as the default empty list.
        Assert.NotNull(decoded.Phones);
        Assert.Empty(decoded.Phones);
    }

    // ---------------------------------------------------------------------
    // TwilioInboundRelayPayload round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public void TwilioInboundRelayPayload_RoundTripsAllFields()
    {
        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = "depA",
            MessageSid = "SM123",
            From = "+15559998888",
            To = "+15551110000",
            Body = "hello",
            NumMedia = 1,
            Media =
            {
                new RelayMediaItem { Index = 0, Url = "https://api.twilio.com/x.jpg", ContentType = "image/jpeg" }
            },
            ReceivedAtUtc = new DateTime(2026, 5, 26, 20, 0, 0, DateTimeKind.Utc),
        };

        string json = JsonSerializer.Serialize(payload, CamelCase);
        TwilioInboundRelayPayload? decoded = JsonSerializer.Deserialize<TwilioInboundRelayPayload>(json, CamelCase);

        Assert.NotNull(decoded);
        Assert.Equal("depA", decoded!.DeploymentId);
        Assert.Equal("SM123", decoded.MessageSid);
        Assert.Equal("+15559998888", decoded.From);
        Assert.Equal("+15551110000", decoded.To);
        Assert.Equal("hello", decoded.Body);
        Assert.Equal(1, decoded.NumMedia);
        Assert.Single(decoded.Media);
        Assert.Equal(0, decoded.Media[0].Index);
        Assert.Equal("https://api.twilio.com/x.jpg", decoded.Media[0].Url);
        Assert.Equal("image/jpeg", decoded.Media[0].ContentType);
        Assert.Equal(DateTimeKind.Utc, decoded.ReceivedAtUtc.Kind);
    }

    [Fact]
    public void TwilioInboundRelayPayload_DefaultsAreSafe()
    {
        TwilioInboundRelayPayload payload = new();

        Assert.Equal(string.Empty, payload.DeploymentId);
        Assert.Equal(string.Empty, payload.MessageSid);
        Assert.Equal(string.Empty, payload.Body);
        Assert.Equal(0, payload.NumMedia);
        Assert.NotNull(payload.Media);
        Assert.Empty(payload.Media);
    }

    // ---------------------------------------------------------------------
    // TwilioStatusRelayPayload round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public void TwilioStatusRelayPayload_RoundTripsAllFields()
    {
        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = "depA",
            MessageSid = "SM999",
            MessageStatus = "delivered",
            ErrorCode = null,
            ReceivedAtUtc = new DateTime(2026, 5, 26, 20, 5, 0, DateTimeKind.Utc),
        };

        string json = JsonSerializer.Serialize(payload, CamelCase);
        TwilioStatusRelayPayload? decoded = JsonSerializer.Deserialize<TwilioStatusRelayPayload>(json, CamelCase);

        Assert.NotNull(decoded);
        Assert.Equal("depA", decoded!.DeploymentId);
        Assert.Equal("SM999", decoded.MessageSid);
        Assert.Equal("delivered", decoded.MessageStatus);
        Assert.Null(decoded.ErrorCode);
    }

    [Fact]
    public void TwilioStatusRelayPayload_PreservesErrorCodeWhenFailed()
    {
        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = "depA",
            MessageSid = "SM999",
            MessageStatus = "failed",
            ErrorCode = "30007",
        };

        string json = JsonSerializer.Serialize(payload, CamelCase);
        TwilioStatusRelayPayload? decoded = JsonSerializer.Deserialize<TwilioStatusRelayPayload>(json, CamelCase);

        Assert.NotNull(decoded);
        Assert.Equal("failed", decoded!.MessageStatus);
        Assert.Equal("30007", decoded.ErrorCode);
    }
}
