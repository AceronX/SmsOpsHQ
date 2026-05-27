using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SmsOpsHQ.Api.HubClient;
using SmsOpsHQ.Api.Webhooks;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Parity tests proving the two store-side ingress paths produce equivalent
/// processor input for the same source data:
///
///  Path A (legacy / dev): Twilio --form--> store TwilioInboundController
///                          → form parse → InboundSmsRequest → processor
///
///  Path B (production):  Twilio --form--> Hub TwilioInboundController
///                          → form parse → TwilioInboundRelayPayload (over SignalR)
///                          → HubSignalRClient.MapToInboundRequest
///                          → InboundSmsRequest → processor
///
/// Because the processor (IInboundSmsProcessor) is the single shared downstream
/// codepath -- already covered end-to-end by <c>InboundSmsProcessorTests</c> --
/// identical InboundSmsRequest values on both ingress paths guarantees
/// identical DB state. That makes this a fast, deterministic substitute for
/// a full WebApplicationFactory round trip, and a regression guard for the
/// pact between the two controllers' parsing rules.
///
/// Covers requirements doc Phase 6.5.
/// </summary>
public class InboundIngressParityTests
{
    private const string Deployment = "deploy-X";

    // ---------- inbound parity ----------

    [Fact]
    public async Task PlainSms_ProducesEquivalentInboundSmsRequestOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_plain_1",
            ["From"] = "+15559876543",
            ["To"] = "+15551234567",
            ["Body"] = "hello world",
            ["NumMedia"] = "0",
        };

        (InboundSmsRequest http, InboundSmsRequest signalr) = await RunBothPathsAsync(form);

        AssertEquivalent(http, signalr);
    }

    [Fact]
    public async Task MmsWithTwoMedia_ProducesEquivalentInboundSmsRequestOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_mms_2",
            ["From"] = "+15559876543",
            ["To"] = "+15551234567",
            ["Body"] = "see attached",
            ["NumMedia"] = "2",
            ["MediaUrl0"] = "https://api.twilio.com/media/A",
            ["MediaContentType0"] = "image/jpeg",
            ["MediaUrl1"] = "https://api.twilio.com/media/B",
            ["MediaContentType1"] = "image/png",
        };

        (InboundSmsRequest http, InboundSmsRequest signalr) = await RunBothPathsAsync(form);

        AssertEquivalent(http, signalr);
        Assert.Equal(2, http.Media.Count);
        Assert.Equal(2, signalr.Media.Count);
    }

    [Fact]
    public async Task MediaWithMissingContentType_ProducesEquivalentInboundSmsRequestOnBothPaths()
    {
        // Twilio sometimes omits MediaContentType{i}; both paths must treat
        // it as null without crashing.
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_mms_noct",
            ["From"] = "+15559876543",
            ["To"] = "+15551234567",
            ["Body"] = "x",
            ["NumMedia"] = "1",
            ["MediaUrl0"] = "https://api.twilio.com/media/A",
        };

        (InboundSmsRequest http, InboundSmsRequest signalr) = await RunBothPathsAsync(form);

        AssertEquivalent(http, signalr);
        Assert.Null(http.Media[0].ContentType);
        Assert.Null(signalr.Media[0].ContentType);
    }

    [Fact]
    public async Task EmptyBody_ProducesEquivalentInboundSmsRequestOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_empty_body",
            ["From"] = "+15559876543",
            ["To"] = "+15551234567",
            ["Body"] = string.Empty,
            ["NumMedia"] = "0",
        };

        (InboundSmsRequest http, InboundSmsRequest signalr) = await RunBothPathsAsync(form);

        AssertEquivalent(http, signalr);
    }

    [Fact]
    public async Task NumMediaMissing_DefaultsToZeroOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_no_nummedia",
            ["From"] = "+15559876543",
            ["To"] = "+15551234567",
            ["Body"] = "x",
            // NumMedia intentionally omitted
        };

        (InboundSmsRequest http, InboundSmsRequest signalr) = await RunBothPathsAsync(form);

        AssertEquivalent(http, signalr);
        Assert.Equal(0, http.NumMedia);
        Assert.Equal(0, signalr.NumMedia);
    }

    // ---------- status parity ----------

    [Fact]
    public async Task StatusCallback_ProducesEquivalentMessageStatusUpdateOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_st_1",
            ["MessageStatus"] = "delivered",
            ["From"] = "+15551234567",
            ["To"] = "+15559876543",
        };

        (MessageStatusUpdate http, MessageStatusUpdate signalr) = await RunBothStatusPathsAsync(form);

        AssertEquivalent(http, signalr);
    }

    [Fact]
    public async Task StatusCallback_WithErrorCode_ProducesEquivalentMessageStatusUpdateOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_st_failed",
            ["MessageStatus"] = "failed",
            ["From"] = "+15551234567",
            ["To"] = "+15559876543",
            ["ErrorCode"] = "30007",
        };

        (MessageStatusUpdate http, MessageStatusUpdate signalr) = await RunBothStatusPathsAsync(form);

        AssertEquivalent(http, signalr);
        Assert.Equal("30007", http.ErrorCode);
        Assert.Equal("30007", signalr.ErrorCode);
    }

    [Fact]
    public async Task StatusCallback_WithoutErrorCode_NullOnBothPaths()
    {
        Dictionary<string, StringValues> form = new()
        {
            ["MessageSid"] = "SM_st_ok",
            ["MessageStatus"] = "delivered",
        };

        (MessageStatusUpdate http, MessageStatusUpdate signalr) = await RunBothStatusPathsAsync(form);

        AssertEquivalent(http, signalr);
        Assert.Null(http.ErrorCode);
        Assert.Null(signalr.ErrorCode);
    }

    // ---------- runners ----------

    private static async Task<(InboundSmsRequest http, InboundSmsRequest signalr)> RunBothPathsAsync(
        Dictionary<string, StringValues> form)
    {
        // Path A: drive the actual store TwilioInboundController.
        RecordingInboundProcessor httpProcessor = new();
        TwilioInboundController controller = new(httpProcessor, NullLogger<TwilioInboundController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContextWithForm(form) };
        await controller.HandleInbound(CancellationToken.None);
        InboundSmsRequest httpRequest = httpProcessor.Calls.Single();

        // Path B: emulate the Hub's "form → relay payload" conversion, then
        // run the store-side mapper that the SignalR handler uses.
        TwilioInboundRelayPayload relayPayload = BuildRelayPayload(form, Deployment);
        InboundSmsRequest signalrRequest = HubSignalRClient.MapToInboundRequest(relayPayload);

        return (httpRequest, signalrRequest);
    }

    private static async Task<(MessageStatusUpdate http, MessageStatusUpdate signalr)> RunBothStatusPathsAsync(
        Dictionary<string, StringValues> form)
    {
        RecordingStatusProcessor httpProcessor = new();
        TwilioStatusController controller = new(httpProcessor, NullLogger<TwilioStatusController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContextWithForm(form) };
        await controller.HandleStatus(CancellationToken.None);
        MessageStatusUpdate httpUpdate = httpProcessor.Calls.Single();

        TwilioStatusRelayPayload relayPayload = BuildStatusRelayPayload(form, Deployment);
        MessageStatusUpdate signalrUpdate = HubSignalRClient.MapToStatusUpdate(relayPayload);

        return (httpUpdate, signalrUpdate);
    }

    // Mirror of SmsOpsHQ.Hub.Server.Webhooks.TwilioInboundController form-to-DTO
    // logic. Keep this in sync with the Hub controller -- this method is the
    // contract pact between the two sides.
    private static TwilioInboundRelayPayload BuildRelayPayload(
        Dictionary<string, StringValues> form, string deploymentId)
    {
        string Get(string key) => form.TryGetValue(key, out StringValues v) ? v.ToString() : string.Empty;
        int numMedia = int.TryParse(Get("NumMedia"), out int nm) ? nm : 0;

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = deploymentId,
            MessageSid = Get("MessageSid"),
            From = Get("From"),
            To = Get("To"),
            Body = Get("Body"),
            NumMedia = numMedia,
            ReceivedAtUtc = DateTime.UtcNow,
        };

        for (int i = 0; i < numMedia; i++)
        {
            string url = Get($"MediaUrl{i}");
            if (string.IsNullOrEmpty(url)) continue;
            payload.Media.Add(new RelayMediaItem
            {
                Index = i,
                Url = url,
                ContentType = form.ContainsKey($"MediaContentType{i}")
                    ? form[$"MediaContentType{i}"].ToString()
                    : null,
            });
        }

        return payload;
    }

    private static TwilioStatusRelayPayload BuildStatusRelayPayload(
        Dictionary<string, StringValues> form, string deploymentId)
    {
        string Get(string key) => form.TryGetValue(key, out StringValues v) ? v.ToString() : string.Empty;

        return new TwilioStatusRelayPayload
        {
            DeploymentId = deploymentId,
            MessageSid = Get("MessageSid"),
            MessageStatus = Get("MessageStatus"),
            ErrorCode = form.ContainsKey("ErrorCode") ? form["ErrorCode"].ToString() : null,
            ReceivedAtUtc = DateTime.UtcNow,
        };
    }

    private static DefaultHttpContext BuildContextWithForm(Dictionary<string, StringValues> form)
    {
        StringBuilder sb = new();
        foreach (KeyValuePair<string, StringValues> kv in form)
        {
            foreach (string? v in kv.Value)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(v ?? string.Empty));
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        DefaultHttpContext ctx = new();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        return ctx;
    }

    private static void AssertEquivalent(InboundSmsRequest a, InboundSmsRequest b)
    {
        Assert.Equal(a.MessageSid, b.MessageSid);
        Assert.Equal(a.From, b.From);
        Assert.Equal(a.To, b.To);
        Assert.Equal(a.Body, b.Body);
        Assert.Equal(a.NumMedia, b.NumMedia);
        Assert.Equal(a.Media.Count, b.Media.Count);
        for (int i = 0; i < a.Media.Count; i++)
        {
            Assert.Equal(a.Media[i].Index, b.Media[i].Index);
            Assert.Equal(a.Media[i].Url, b.Media[i].Url);
            Assert.Equal(a.Media[i].ContentType, b.Media[i].ContentType);
        }
        // ReceivedAtUtc is intentionally NOT compared: each ingress path
        // stamps its own arrival time. The processor accepts a null/value
        // either way -- the actual SMS arrival time is preserved upstream
        // via MessageSid.
    }

    private static void AssertEquivalent(MessageStatusUpdate a, MessageStatusUpdate b)
    {
        Assert.Equal(a.MessageSid, b.MessageSid);
        Assert.Equal(a.MessageStatus, b.MessageStatus);
        Assert.Equal(a.ErrorCode, b.ErrorCode);
        // ReceivedAtUtc intentionally NOT compared (same reason as inbound).
    }

    // ---------- fakes ----------

    private sealed class RecordingInboundProcessor : IInboundSmsProcessor
    {
        public List<InboundSmsRequest> Calls { get; } = new();

        public Task<InboundSmsProcessingResult> ProcessAsync(InboundSmsRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(new InboundSmsProcessingResult(InboundSmsResultKind.Processed, 1, 1, 1));
        }
    }

    private sealed class RecordingStatusProcessor : IMessageStatusProcessor
    {
        public List<MessageStatusUpdate> Calls { get; } = new();

        public Task<MessageStatusProcessingResult> ProcessAsync(MessageStatusUpdate update, CancellationToken cancellationToken = default)
        {
            Calls.Add(update);
            return Task.FromResult(new MessageStatusProcessingResult(MessageStatusResultKind.Updated, 1, 1, 1));
        }
    }
}
