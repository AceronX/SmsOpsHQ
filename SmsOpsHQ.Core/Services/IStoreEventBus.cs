namespace SmsOpsHQ.Core.Services;

/// <summary>
/// In-process pub/sub the store-side services use to broadcast user-visible
/// activity (SMS sent / SMS received / thread marked read / etc.) to interested
/// subscribers.
///
/// The primary subscriber is <c>HeartbeatPusher</c>, which uses these events
/// to push a fresh heartbeat to HQ within a couple of seconds so the Hub
/// dashboard's per-store counters (sent today / received today / unread) and
/// derived state (last activity, online flag) update in near-real-time
/// instead of waiting for the next periodic tick (up to 60 s late).
///
/// Contract:
///   - Producers call <see cref="NotifyActivity"/> in fire-and-forget style.
///     Implementations MUST swallow subscriber exceptions so a faulty
///     subscriber cannot break the SMS send/receive code path.
///   - Subscribers are invoked synchronously on the producer's thread, so
///     they MUST be fast (e.g. set a flag, queue a timer) and never block.
/// </summary>
public interface IStoreEventBus
{
    /// <summary>Fired every time a producer calls <see cref="NotifyActivity"/>.</summary>
    event Action<StoreActivityEvent>? ActivityChanged;

    /// <summary>
    /// Publish an activity event. Safe to call from any thread. Never throws.
    /// <paramref name="source"/> is a short machine code such as
    /// <c>"sms.sent"</c>, <c>"sms.received"</c>, <c>"thread.read"</c>,
    /// <c>"note.created"</c>. Sources are not enforced; new producers can
    /// add their own codes without coordination -- the heartbeat carries
    /// the resulting state, not the source code itself.
    /// </summary>
    void NotifyActivity(string source);
}

/// <summary>
/// Payload for <see cref="IStoreEventBus.ActivityChanged"/>. <see cref="AtUtc"/>
/// is set by the bus so subscribers don't have to read the clock again.
/// </summary>
public sealed record StoreActivityEvent(string Source, DateTime AtUtc);

/// <summary>
/// Default in-process implementation backed by a plain <c>event</c>. Singleton
/// lifetime: every producer/consumer in the API process shares one bus.
/// </summary>
public sealed class StoreEventBus : IStoreEventBus
{
    public event Action<StoreActivityEvent>? ActivityChanged;

    public void NotifyActivity(string source)
    {
        // Snapshot the delegate so concurrent subscribe/unsubscribe doesn't race.
        Action<StoreActivityEvent>? handler = ActivityChanged;
        if (handler is null) return;

        StoreActivityEvent payload = new(string.IsNullOrWhiteSpace(source) ? "unknown" : source, DateTime.UtcNow);

        foreach (Action<StoreActivityEvent> sub in handler.GetInvocationList().Cast<Action<StoreActivityEvent>>())
        {
            try
            {
                sub(payload);
            }
            catch
            {
                // Bus must not let one bad subscriber break the rest, and must
                // NEVER throw back into the producer (which is a SMS pipeline).
            }
        }
    }
}
