using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

/// <summary>
/// Single source of truth for the store-side inbound-SMS pipeline:
/// idempotency, store routing, validation, opt-out, identity, threading,
/// media, realtime push. Implementations are transport-agnostic; both the
/// HTTP webhook (<c>TwilioInboundController</c>) and the Hub SignalR receiver
/// call this so they cannot diverge.
/// </summary>
public interface IInboundSmsProcessor
{
    Task<InboundSmsProcessingResult> ProcessAsync(InboundSmsRequest request, CancellationToken cancellationToken = default);
}
