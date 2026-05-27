using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.HubClient;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the SignalR receive handlers in <see cref="HubSignalRClient"/>.
/// The handler bodies were intentionally extracted into internal methods so
/// we can exercise them without spinning up a SignalR server.
/// </summary>
public class HubSignalRClientHandlerTests
{
    private const string MyDeployment = "store-alpha";

    // -------- HandleInboundRelayAsync --------

    [Fact]
    public async Task HandleInboundRelay_DeploymentMatches_DispatchesToProcessor()
    {
        RecordingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(MyDeployment, processor, statusProcessor: new RecordingStatusProcessor());

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = MyDeployment,
            MessageSid = "SM_in_1",
            From = "+15559876543",
            To = "+15551234567",
            Body = "hello hub-relayed",
            NumMedia = 0,
        };

        await client.HandleInboundRelayAsync(payload, CancellationToken.None);

        InboundSmsRequest request = Assert.Single(processor.Calls);
        Assert.Equal("SM_in_1", request.MessageSid);
        Assert.Equal("+15559876543", request.From);
        Assert.Equal("+15551234567", request.To);
        Assert.Equal("hello hub-relayed", request.Body);
        Assert.Empty(request.Media);
    }

    [Fact]
    public async Task HandleInboundRelay_DeploymentMismatch_DoesNotDispatch()
    {
        RecordingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(MyDeployment, processor, statusProcessor: new RecordingStatusProcessor());

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = "store-beta", // not us
            MessageSid = "SM_in_misaddressed",
            From = "+15559999999",
            To = "+15551112222",
            Body = "should be dropped",
        };

        await client.HandleInboundRelayAsync(payload, CancellationToken.None);

        Assert.Empty(processor.Calls);
    }

    [Fact]
    public async Task HandleInboundRelay_DeploymentCaseInsensitive_StillDispatches()
    {
        RecordingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(MyDeployment, processor, statusProcessor: new RecordingStatusProcessor());

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = MyDeployment.ToUpperInvariant(),
            MessageSid = "SM_case",
            From = "+15559876543",
            To = "+15551234567",
            Body = "x",
        };

        await client.HandleInboundRelayAsync(payload, CancellationToken.None);

        Assert.Single(processor.Calls);
    }

    [Fact]
    public async Task HandleInboundRelay_OurDeploymentEmpty_DropsPayload()
    {
        // If the store hasn't configured Hub:DeploymentId we cannot safely
        // match any payload, so everything must be dropped (defense in depth;
        // in practice SignalR connect wouldn't even succeed in this state).
        RecordingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(deploymentId: "", processor, statusProcessor: new RecordingStatusProcessor());

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = MyDeployment,
            MessageSid = "SM_noselfid",
            From = "+1", To = "+2", Body = "x",
        };

        await client.HandleInboundRelayAsync(payload, CancellationToken.None);

        Assert.Empty(processor.Calls);
    }

    [Fact]
    public async Task HandleInboundRelay_NullPayload_DropsCleanly()
    {
        RecordingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(MyDeployment, processor, statusProcessor: new RecordingStatusProcessor());

        await client.HandleInboundRelayAsync(null!, CancellationToken.None);

        Assert.Empty(processor.Calls);
    }

    [Fact]
    public async Task HandleInboundRelay_ProcessorThrows_BubblesOut()
    {
        // The Task.Run wrapper in HookCommandHandlers swallows exceptions; the
        // handler method itself does NOT swallow so unit tests can see them.
        ThrowingInboundProcessor processor = new();
        HubSignalRClient client = BuildClient(MyDeployment, processor, statusProcessor: new RecordingStatusProcessor());

        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = MyDeployment,
            MessageSid = "SM_boom",
            From = "+1", To = "+2", Body = "x",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.HandleInboundRelayAsync(payload, CancellationToken.None));
    }

    // -------- HandleStatusRelayAsync --------

    [Fact]
    public async Task HandleStatusRelay_DeploymentMatches_DispatchesToStatusProcessor()
    {
        RecordingStatusProcessor status = new();
        HubSignalRClient client = BuildClient(MyDeployment, inboundProcessor: new RecordingInboundProcessor(), status);

        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = MyDeployment,
            MessageSid = "SM_st",
            MessageStatus = "delivered",
            ErrorCode = null,
        };

        await client.HandleStatusRelayAsync(payload, CancellationToken.None);

        MessageStatusUpdate update = Assert.Single(status.Calls);
        Assert.Equal("SM_st", update.MessageSid);
        Assert.Equal("delivered", update.MessageStatus);
    }

    [Fact]
    public async Task HandleStatusRelay_DeploymentMismatch_DoesNotDispatch()
    {
        RecordingStatusProcessor status = new();
        HubSignalRClient client = BuildClient(MyDeployment, inboundProcessor: new RecordingInboundProcessor(), status);

        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = "store-beta",
            MessageSid = "SM_other",
            MessageStatus = "delivered",
        };

        await client.HandleStatusRelayAsync(payload, CancellationToken.None);

        Assert.Empty(status.Calls);
    }

    [Fact]
    public async Task HandleStatusRelay_ForwardsErrorCode()
    {
        RecordingStatusProcessor status = new();
        HubSignalRClient client = BuildClient(MyDeployment, inboundProcessor: new RecordingInboundProcessor(), status);

        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = MyDeployment,
            MessageSid = "SM_fail",
            MessageStatus = "failed",
            ErrorCode = "30007",
        };

        await client.HandleStatusRelayAsync(payload, CancellationToken.None);

        Assert.Equal("30007", status.Calls[0].ErrorCode);
    }

    // -------- Mappers --------

    [Fact]
    public void MapToInboundRequest_CopiesAllFieldsAndFiltersBlankMedia()
    {
        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = "store-x",
            MessageSid = "SM_x",
            From = "+15559876543",
            To = "+15551234567",
            Body = "see attached",
            NumMedia = 2,
            ReceivedAtUtc = new DateTime(2026, 5, 26, 20, 0, 0, DateTimeKind.Utc),
        };
        payload.Media.Add(new RelayMediaItem { Index = 0, Url = "https://twilio/media/A", ContentType = "image/jpeg" });
        payload.Media.Add(new RelayMediaItem { Index = 1, Url = "", ContentType = null });

        InboundSmsRequest request = HubSignalRClient.MapToInboundRequest(payload);

        Assert.Equal("SM_x", request.MessageSid);
        Assert.Equal("+15559876543", request.From);
        Assert.Equal("+15551234567", request.To);
        Assert.Equal("see attached", request.Body);
        Assert.Equal(2, request.NumMedia);
        Assert.Equal(new DateTime(2026, 5, 26, 20, 0, 0, DateTimeKind.Utc), request.ReceivedAtUtc);
        InboundMediaItem media = Assert.Single(request.Media);
        Assert.Equal("https://twilio/media/A", media.Url);
        Assert.Equal("image/jpeg", media.ContentType);
    }

    [Fact]
    public void MapToInboundRequest_NullStrings_BecomeEmpty()
    {
        TwilioInboundRelayPayload payload = new()
        {
            DeploymentId = "store-x",
            MessageSid = null!,
            From = null!,
            To = null!,
            Body = null!,
            Media = null!,
        };

        InboundSmsRequest request = HubSignalRClient.MapToInboundRequest(payload);

        Assert.Equal(string.Empty, request.MessageSid);
        Assert.Equal(string.Empty, request.From);
        Assert.Equal(string.Empty, request.To);
        Assert.Equal(string.Empty, request.Body);
        Assert.Empty(request.Media);
    }

    [Fact]
    public void MapToStatusUpdate_CopiesAllFields()
    {
        TwilioStatusRelayPayload payload = new()
        {
            DeploymentId = "x",
            MessageSid = "SM_st",
            MessageStatus = "delivered",
            ErrorCode = "30007",
            ReceivedAtUtc = new DateTime(2026, 5, 26, 20, 0, 0, DateTimeKind.Utc),
        };

        MessageStatusUpdate update = HubSignalRClient.MapToStatusUpdate(payload);

        Assert.Equal("SM_st", update.MessageSid);
        Assert.Equal("delivered", update.MessageStatus);
        Assert.Equal("30007", update.ErrorCode);
        Assert.Equal(new DateTime(2026, 5, 26, 20, 0, 0, DateTimeKind.Utc), update.ReceivedAtUtc);
    }

    // -------- helpers --------

    private static HubSignalRClient BuildClient(
        string deploymentId,
        IInboundSmsProcessor inboundProcessor,
        IMessageStatusProcessor statusProcessor)
    {
        FakeHeartbeatPusher pusher = new() { DeploymentId = deploymentId };

        ServiceCollection services = new();
        services.AddSingleton(inboundProcessor);
        services.AddSingleton(statusProcessor);
        ServiceProvider provider = services.BuildServiceProvider();

        return new HubSignalRClient(
            pusher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HubSignalRClient>.Instance);
    }

    // -------- fakes --------

    private sealed class FakeHeartbeatPusher : IHeartbeatPusher
    {
        public string HubUrl { get; set; } = "https://hub.test";
        public string StoreKey { get; set; } = "test-key";
        public string DeploymentId { get; set; } = string.Empty;
        public int IntervalSeconds { get; set; } = 60;
        public bool IsConfigured => true;

        public void Start() { }
        public void Stop() { }
        public Task<bool> SendOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public HeartbeatPusherStatus GetStatus() => new();
        public Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HeartbeatPayload { DeploymentId = DeploymentId });
    }

    private sealed class RecordingInboundProcessor : IInboundSmsProcessor
    {
        public List<InboundSmsRequest> Calls { get; } = new();

        public Task<InboundSmsProcessingResult> ProcessAsync(InboundSmsRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(new InboundSmsProcessingResult(InboundSmsResultKind.Processed, 1, 2, 3));
        }
    }

    private sealed class ThrowingInboundProcessor : IInboundSmsProcessor
    {
        public Task<InboundSmsProcessingResult> ProcessAsync(InboundSmsRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class RecordingStatusProcessor : IMessageStatusProcessor
    {
        public List<MessageStatusUpdate> Calls { get; } = new();

        public Task<MessageStatusProcessingResult> ProcessAsync(MessageStatusUpdate update, CancellationToken cancellationToken = default)
        {
            Calls.Add(update);
            return Task.FromResult(new MessageStatusProcessingResult(MessageStatusResultKind.Updated, 1, 2, 3));
        }
    }
}
