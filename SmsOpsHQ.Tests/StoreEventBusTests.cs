using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the in-process <see cref="StoreEventBus"/> used by the store-side
/// SMS pipeline to wake the HeartbeatPusher when something HQ-visible happens
/// (SMS sent/received, thread read, etc.). The bus contract is:
///
///   - Subscribers get every <c>NotifyActivity</c> call.
///   - One subscriber throwing must NOT prevent other subscribers from running.
///   - <c>NotifyActivity</c> must never throw back into the producer (which is
///     the SMS send/receive code path).
///   - Empty/null source codes are normalized to a safe value.
/// </summary>
public sealed class StoreEventBusTests
{
    [Fact]
    public void NotifyActivity_NoSubscribers_DoesNotThrow()
    {
        StoreEventBus bus = new();
        // No exception = pass. Confirms the producer side is safe to call
        // even before anyone subscribes (e.g. at API startup).
        bus.NotifyActivity("sms.sent");
    }

    [Fact]
    public void NotifyActivity_SingleSubscriber_ReceivesEvent()
    {
        StoreEventBus bus = new();
        StoreActivityEvent? received = null;
        bus.ActivityChanged += e => received = e;

        bus.NotifyActivity("sms.received");

        Assert.NotNull(received);
        Assert.Equal("sms.received", received!.Source);
        Assert.True(received.AtUtc > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void NotifyActivity_MultipleSubscribers_AllReceiveEvent()
    {
        StoreEventBus bus = new();
        int a = 0, b = 0, c = 0;
        bus.ActivityChanged += _ => a++;
        bus.ActivityChanged += _ => b++;
        bus.ActivityChanged += _ => c++;

        bus.NotifyActivity("thread.read");
        bus.NotifyActivity("sms.sent");

        Assert.Equal(2, a);
        Assert.Equal(2, b);
        Assert.Equal(2, c);
    }

    [Fact]
    public void NotifyActivity_OneSubscriberThrows_OthersStillRun()
    {
        StoreEventBus bus = new();
        int after = 0;
        bus.ActivityChanged += _ => throw new InvalidOperationException("kaboom");
        bus.ActivityChanged += _ => after++;

        // Producer is the SMS pipeline -- a buggy subscriber MUST NOT break it.
        bus.NotifyActivity("sms.sent");

        Assert.Equal(1, after);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotifyActivity_BlankSource_NormalizedToUnknown(string? source)
    {
        StoreEventBus bus = new();
        StoreActivityEvent? captured = null;
        bus.ActivityChanged += e => captured = e;

        bus.NotifyActivity(source!);

        Assert.NotNull(captured);
        Assert.Equal("unknown", captured!.Source);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        StoreEventBus bus = new();
        int count = 0;
        Action<StoreActivityEvent> handler = _ => count++;
        bus.ActivityChanged += handler;

        bus.NotifyActivity("a");
        bus.ActivityChanged -= handler;
        bus.NotifyActivity("b");

        Assert.Equal(1, count);
    }
}
