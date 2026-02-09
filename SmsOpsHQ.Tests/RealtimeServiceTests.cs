using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Infrastructure.Hubs;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for RealtimeService and SmsOpsHub group naming.
// Uses a fake IHubContext to verify broadcast calls without a real server.
public class RealtimeServiceTests
{
    // ── SmsOpsHub group naming ───────────────────────────────────────

    [Fact]
    public void StoreGroupName_FormatsCorrectly()
    {
        Assert.Equal("store_1", SmsOpsHub.StoreGroupName(1));
        Assert.Equal("store_42", SmsOpsHub.StoreGroupName(42));
        Assert.Equal("store_0", SmsOpsHub.StoreGroupName(0));
    }

    // ── RealtimeService broadcast tests using FakeHubContext ─────────

    [Fact]
    public async Task PushMessageNewAsync_SendsToCorrectGroup()
    {
        FakeHubContext fakeHub = new FakeHubContext();
        RealtimeService service = new RealtimeService(fakeHub, NullLogger<RealtimeService>.Instance);

        MessageDto messageDto = new MessageDto
        {
            MessageId = 1,
            ThreadId = 10,
            Direction = "Inbound",
            FromE164 = "+15559876543",
            ToE164 = "+15551234567",
            Body = "Hello",
            Status = "Received"
        };
        ThreadDto threadDto = new ThreadDto
        {
            ThreadId = 10,
            StoreId = 5,
            Status = "Open",
            UnreadCount = 1
        };

        await service.PushMessageNewAsync(5, 10, messageDto, threadDto);

        Assert.Single(fakeHub.Sends);
        FakeHubContext.SendRecord send = fakeHub.Sends[0];
        Assert.Equal("store_5", send.GroupName);
        Assert.Equal("MessageNew", send.Method);
    }

    [Fact]
    public async Task PushMessageStatusAsync_SendsToCorrectGroup()
    {
        FakeHubContext fakeHub = new FakeHubContext();
        RealtimeService service = new RealtimeService(fakeHub, NullLogger<RealtimeService>.Instance);

        await service.PushMessageStatusAsync(3, 20, 100, "SM_abc", "Delivered", null);

        Assert.Single(fakeHub.Sends);
        FakeHubContext.SendRecord send = fakeHub.Sends[0];
        Assert.Equal("store_3", send.GroupName);
        Assert.Equal("MessageStatus", send.Method);
    }

    [Fact]
    public async Task PushSystemAlertAsync_SendsToCorrectGroup()
    {
        FakeHubContext fakeHub = new FakeHubContext();
        RealtimeService service = new RealtimeService(fakeHub, NullLogger<RealtimeService>.Instance);

        await service.PushSystemAlertAsync(7, "RATE_LIMIT", "Rate limit exceeded", "Warning");

        Assert.Single(fakeHub.Sends);
        FakeHubContext.SendRecord send = fakeHub.Sends[0];
        Assert.Equal("store_7", send.GroupName);
        Assert.Equal("SystemAlert", send.Method);
    }

    [Fact]
    public async Task MultiplePushes_AllRecorded()
    {
        FakeHubContext fakeHub = new FakeHubContext();
        RealtimeService service = new RealtimeService(fakeHub, NullLogger<RealtimeService>.Instance);

        await service.PushSystemAlertAsync(1, "A", "Alert A", "Info");
        await service.PushSystemAlertAsync(2, "B", "Alert B", "Error");
        await service.PushMessageStatusAsync(1, 10, 50, "SM_x", "Failed", "30007");

        Assert.Equal(3, fakeHub.Sends.Count);
        Assert.Equal("store_1", fakeHub.Sends[0].GroupName);
        Assert.Equal("store_2", fakeHub.Sends[1].GroupName);
        Assert.Equal("store_1", fakeHub.Sends[2].GroupName);
    }

    // ── Fake IHubContext for testing ─────────────────────────────────

    private sealed class FakeHubContext : IHubContext<SmsOpsHub>
    {
        public List<SendRecord> Sends { get; } = new();

        public IHubClients Clients => new FakeHubClients(this);
        public IGroupManager Groups => throw new NotImplementedException();

        public sealed record SendRecord(string GroupName, string Method, object? Arg);

        private sealed class FakeHubClients : IHubClients
        {
            private readonly FakeHubContext _owner;
            public FakeHubClients(FakeHubContext owner) { _owner = owner; }

            public IClientProxy All => throw new NotImplementedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
                throw new NotImplementedException();
            public IClientProxy Client(string connectionId) =>
                throw new NotImplementedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) =>
                throw new NotImplementedException();
            public IClientProxy Group(string groupName) =>
                new FakeClientProxy(_owner, groupName);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
                throw new NotImplementedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
                throw new NotImplementedException();
            public IClientProxy User(string userId) =>
                throw new NotImplementedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) =>
                throw new NotImplementedException();
        }

        private sealed class FakeClientProxy : IClientProxy
        {
            private readonly FakeHubContext _owner;
            private readonly string _groupName;

            public FakeClientProxy(FakeHubContext owner, string groupName)
            {
                _owner = owner;
                _groupName = groupName;
            }

            public Task SendCoreAsync(string method, object?[] args,
                CancellationToken cancellationToken = default)
            {
                _owner.Sends.Add(new SendRecord(_groupName, method, args.Length > 0 ? args[0] : null));
                return Task.CompletedTask;
            }
        }
    }
}
