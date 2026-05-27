using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Infrastructure.Services;

/// <summary>
/// Store-side inbound-SMS pipeline, factored out of <c>TwilioInboundController</c>
/// so the HTTP and SignalR transports share one implementation.
///
/// Pipeline (must remain consistent with the legacy controller behavior):
///   1. Idempotency check by Twilio SID.
///   2. Resolve owning store by <c>To</c> then <c>From</c>.
///   3. Validate message metadata + body.
///   4. Handle STOP keywords (opt-out compliance).
///   5. Resolve customer identity (best-effort).
///   6. Find/create Customer + Thread.
///   7. Reopen closed thread.
///   8. Serialize media JSON.
///   9. Classify + create inbound message + stamp SID.
///   10. Bump thread + increment unread.
///   11. Realtime push.
/// </summary>
public sealed class InboundSmsProcessor : IInboundSmsProcessor
{
    // Keep in sync with TwilioInboundController for behavioral parity until
    // that class is fully removed.
    private static readonly string[] StopKeywords = { "STOP", "UNSUBSCRIBE", "CANCEL", "END", "QUIT" };

    private readonly IMessageRepository _messageRepo;
    private readonly IThreadRepository _threadRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IOptOutRepository _optOutRepo;
    private readonly IStorePhoneResolver _storePhoneResolver;
    private readonly IPhoneValidationService _phoneValidation;
    private readonly IQuarantineService _quarantineService;
    private readonly IIdentityResolver _identityResolver;
    private readonly IRealtimeService _realtimeService;
    private readonly IStoreEventBus? _eventBus;
    private readonly ILogger<InboundSmsProcessor> _logger;

    public InboundSmsProcessor(
        IMessageRepository messageRepo,
        IThreadRepository threadRepo,
        ICustomerRepository customerRepo,
        IOptOutRepository optOutRepo,
        IStorePhoneResolver storePhoneResolver,
        IPhoneValidationService phoneValidation,
        IQuarantineService quarantineService,
        IIdentityResolver identityResolver,
        IRealtimeService realtimeService,
        ILogger<InboundSmsProcessor> logger,
        IStoreEventBus? eventBus = null)
    {
        _messageRepo = messageRepo;
        _threadRepo = threadRepo;
        _customerRepo = customerRepo;
        _optOutRepo = optOutRepo;
        _storePhoneResolver = storePhoneResolver;
        _phoneValidation = phoneValidation;
        _quarantineService = quarantineService;
        _identityResolver = identityResolver;
        _realtimeService = realtimeService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<InboundSmsProcessingResult> ProcessAsync(InboundSmsRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return new InboundSmsProcessingResult(InboundSmsResultKind.Error, null, null, null, "Request was null.");

        string from = request.From ?? string.Empty;
        string to = request.To ?? string.Empty;
        string body = request.Body ?? string.Empty;
        string messageSid = request.MessageSid ?? string.Empty;

        // 1. Idempotency
        Message? existing = await _messageRepo.FindBySidAsync(messageSid, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Inbound SMS {Sid} already exists; skipping (duplicate).", messageSid);
            return new InboundSmsProcessingResult(
                InboundSmsResultKind.Duplicate,
                existing.StoreId,
                existing.ThreadId,
                existing.MessageId,
                "duplicate_sid");
        }

        // 2. Resolve store (To first, fall back to From)
        Store? storeByTo = await _storePhoneResolver.GetStoreByPhoneAsync(to, cancellationToken);
        Store? store = storeByTo ?? await _storePhoneResolver.GetStoreByPhoneAsync(from, cancellationToken);

        if (store is null)
        {
            _logger.LogWarning(
                "Inbound SMS: no store matches To={To} or From={From}. sid={Sid}",
                to, from, messageSid);
            return new InboundSmsProcessingResult(InboundSmsResultKind.NoStoreMatch, null, null, null, "no_store_match");
        }

        string? storePhone = await _storePhoneResolver.GetStorePhoneAsync(store.StoreId, cancellationToken);
        string storePhoneE164 = storePhone ?? to;

        // 3. Validate (body + metadata)
        PhoneValidationResult validationResult = _phoneValidation.ValidateMessage(
            to, from, body, storePhoneE164, store.StoreId);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Inbound SMS validation failed: {Reason}. To={To} From={From} Store={StoreId} Sid={Sid}",
                validationResult.FailureReason, to, from, store.StoreId, messageSid);

            if (validationResult.ShouldQuarantine)
            {
                try
                {
                    int quarantineId = await _quarantineService.QuarantineMessageAsync(
                        store.StoreId, from, to, body, mediaJson: null, messageSid,
                        validationResult.FailureReason ?? "validation_failed",
                        cancellationToken);
                    return new InboundSmsProcessingResult(
                        InboundSmsResultKind.Quarantined,
                        store.StoreId,
                        null,
                        quarantineId,
                        validationResult.FailureReason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to quarantine inbound SMS {Sid}", messageSid);
                    return new InboundSmsProcessingResult(
                        InboundSmsResultKind.Error,
                        store.StoreId,
                        null,
                        null,
                        "quarantine_failed");
                }
            }

            return new InboundSmsProcessingResult(
                InboundSmsResultKind.Rejected,
                store.StoreId,
                null,
                null,
                validationResult.FailureReason);
        }

        // 4. STOP keywords (opt-out)
        string bodyTrimmed = body.Trim().ToUpperInvariant();
        if (StopKeywords.Contains(bodyTrimmed))
        {
            await _optOutRepo.AddAsync(store.StoreId, from, "TwilioSTOP", cancellationToken);
        }

        // 5. Identity (optional; unknown phones still accepted)
        int? identityId = await _identityResolver.ResolveIdentityIdAsync(
            store.StoreId, from, cancellationToken);

        if (identityId is null)
        {
            _logger.LogInformation(
                "Phone {From} not found in XPD for store {StoreId}; accepting as unknown contact. sid={Sid}",
                from, store.StoreId, messageSid);
        }

        // 6. Customer + Thread
        Customer customer = await _customerRepo.FindOrCreateAsync(
            store.StoreId, from, cancellationToken);

        Core.Entities.Thread thread = await _threadRepo.FindOrCreateAsync(
            store.StoreId, identityId, customer.CustomerId, cancellationToken);

        if (thread.CustomerId is null || thread.CustomerId == 0)
        {
            await _threadRepo.UpdateCustomerIdAsync(thread.ThreadId, customer.CustomerId, cancellationToken);
        }

        // 7. Reopen closed thread
        if (thread.Status == "Closed")
        {
            // The LastMessageAt bump below will reopen it; keep the in-memory
            // status flag in sync so the realtime payload matches the DB.
            thread.Status = "Open";
        }

        // 8. Media JSON
        string? mediaJson = BuildMediaJson(request.Media, request.NumMedia);

        // 9. Classify + create + stamp SID
        string category = MessageClassifier.Classify(body);

        Message message = await _messageRepo.CreateInboundAsync(
            store.StoreId, thread.ThreadId, storePhoneE164,
            from, to, body, mediaJson, category,
            cancellationToken);

        // Set TwilioSid (IMessageRepository.CreateInboundAsync does not accept SID at creation).
        await _messageRepo.UpdateSentAsync(message.MessageId, messageSid, "Received", cancellationToken);

        // 10. Bump thread + increment unread
        await _threadRepo.UpdateLastMessageAtAsync(thread.ThreadId, message.CreatedAt, cancellationToken);
        await _threadRepo.IncrementUnreadAsync(thread.ThreadId, cancellationToken);

        // 11. Realtime push
        MessageDto messageDto = new()
        {
            MessageId = message.MessageId,
            ThreadId = thread.ThreadId,
            Direction = "Inbound",
            FromE164 = from,
            ToE164 = to,
            Body = body,
            Status = "Received",
            TwilioSid = messageSid,
            MediaJson = mediaJson,
            Category = category,
            CreatedAt = message.CreatedAt,
        };

        ThreadDto threadDto = new()
        {
            ThreadId = thread.ThreadId,
            StoreId = store.StoreId,
            LastMessageAt = message.CreatedAt,
            UnreadCount = thread.UnreadCount + 1,
            Status = "Open",
        };

        await _realtimeService.PushMessageNewAsync(
            store.StoreId, thread.ThreadId, messageDto, threadDto, cancellationToken);

        // Wake the HeartbeatPusher so the Hub dashboard's received/unread
        // counters refresh within a couple seconds instead of waiting for the
        // next periodic heartbeat (up to IntervalSeconds late). Bus is optional
        // for back-compat with legacy callers / tests that don't wire one.
        _eventBus?.NotifyActivity("sms.received");

        _logger.LogInformation(
            "Inbound SMS processed: store={StoreId} thread={ThreadId} message={MessageId} sid={Sid}",
            store.StoreId, thread.ThreadId, message.MessageId, messageSid);

        return new InboundSmsProcessingResult(
            InboundSmsResultKind.Processed,
            store.StoreId,
            thread.ThreadId,
            message.MessageId);
    }

    /// <summary>
    /// Build the same JSON shape the legacy controller produced
    /// (<c>[{ url, content_type }, ...]</c>) so existing inbox rendering keeps working.
    /// Returns null when there is nothing to record.
    /// </summary>
    private static string? BuildMediaJson(List<InboundMediaItem> media, int numMedia)
    {
        if (media is null || media.Count == 0 || numMedia <= 0) return null;

        List<object> list = new();
        foreach (InboundMediaItem item in media.OrderBy(m => m.Index))
        {
            if (string.IsNullOrEmpty(item.Url)) continue;
            list.Add(new { url = item.Url, content_type = item.ContentType });
        }
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }
}
