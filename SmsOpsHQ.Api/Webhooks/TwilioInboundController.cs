using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Api.Webhooks;

// Handles inbound SMS from Twilio. No [Authorize] -- Twilio sends unauthenticated webhooks.
// Route: POST /twilio-sms
//
// Full inbound pipeline:
//   1. Idempotency check (duplicate SID)
//   2. Resolve store by To/From phone
//   3. Validate message (metadata + body)
//   4. Handle STOP keywords (opt-out)
//   5. Resolve customer identity
//   6. Find/create thread
//   7. Download media (if any)
//   8. Classify message
//   9. Create inbound message record
//  10. Bump thread + increment unread
//  11. Push realtime update
//  12. Return TwiML acknowledgement
[ApiController]
[Route("twilio-sms")]
public sealed class TwilioInboundController : ControllerBase
{
    private readonly IMessageRepository _messageRepo;
    private readonly IThreadRepository _threadRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IOptOutRepository _optOutRepo;
    private readonly IStorePhoneResolver _storePhoneResolver;
    private readonly IPhoneValidationService _phoneValidation;
    private readonly IQuarantineService _quarantineService;
    private readonly IIdentityResolver _identityResolver;
    private readonly IRealtimeService _realtimeService;
    private readonly ILogger<TwilioInboundController> _logger;

    private static readonly string[] StopKeywords = { "STOP", "UNSUBSCRIBE", "CANCEL", "END", "QUIT" };

    public TwilioInboundController(
        IMessageRepository messageRepo,
        IThreadRepository threadRepo,
        ICustomerRepository customerRepo,
        IOptOutRepository optOutRepo,
        IStorePhoneResolver storePhoneResolver,
        IPhoneValidationService phoneValidation,
        IQuarantineService quarantineService,
        IIdentityResolver identityResolver,
        IRealtimeService realtimeService,
        ILogger<TwilioInboundController> logger)
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
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleInbound(CancellationToken cancellationToken)
    {
        IFormCollection form = await Request.ReadFormAsync(cancellationToken);

        string fromPhone = form["From"].ToString();
        string toPhone = form["To"].ToString();
        string body = form["Body"].ToString();
        string messageSid = form["MessageSid"].ToString();
        int numMedia = int.TryParse(form["NumMedia"], out int nm) ? nm : 0;

        // 1. Idempotency: skip if SID already exists
        Message? existing = await _messageRepo.FindBySidAsync(messageSid, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Message {Sid} already exists. Skipping.", messageSid);
            return TwimlOk();
        }

        // 2. Resolve store by To phone, then fallback to From phone
        Store? storeByTo = await _storePhoneResolver.GetStoreByPhoneAsync(toPhone, cancellationToken);
        Store? storeByFrom = storeByTo ?? await _storePhoneResolver.GetStoreByPhoneAsync(fromPhone, cancellationToken);
        Store? store = storeByTo ?? storeByFrom;

        if (store is null)
        {
            _logger.LogWarning("No store found for To={To} or From={From}. Sid={Sid}", toPhone, fromPhone, messageSid);
            return TwimlOk();
        }

        string? storePhone = await _storePhoneResolver.GetStorePhoneAsync(store.StoreId, cancellationToken);
        string storePhoneE164 = storePhone ?? toPhone;

        // 3. Validate message (metadata + body content)
        PhoneValidationResult validationResult = _phoneValidation.ValidateMessage(
            toPhone, fromPhone, body, storePhoneE164, store.StoreId);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Validation failed: {Reason}. To={To}, From={From}, Store={StoreId}, Sid={Sid}",
                validationResult.FailureReason, toPhone, fromPhone, store.StoreId, messageSid);

            if (validationResult.ShouldQuarantine)
            {
                try
                {
                    await _quarantineService.QuarantineMessageAsync(
                        store.StoreId, fromPhone, toPhone, body, null, messageSid,
                        validationResult.FailureReason ?? "validation_failed",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to quarantine message {Sid}", messageSid);
                }
            }

            return TwimlOk();
        }

        // 4. Handle STOP keywords (opt-out compliance)
        string bodyTrimmed = body.Trim().ToUpperInvariant();
        if (StopKeywords.Contains(bodyTrimmed))
        {
            await _optOutRepo.AddAsync(store.StoreId, fromPhone, "TwilioSTOP", cancellationToken);
        }

        // 5. Resolve customer identity (optional — unknown phones are allowed)
        int? identityId = await _identityResolver.ResolveIdentityIdAsync(
            store.StoreId, fromPhone, cancellationToken);

        if (identityId is null)
        {
            _logger.LogInformation(
                "Phone {From} not found in XPD for store {StoreId}; accepting as unknown contact. Sid={Sid}",
                fromPhone, store.StoreId, messageSid);
        }

        // 6. Find/create customer first, then find/create thread by identity OR customer
        Customer customer = await _customerRepo.FindOrCreateAsync(
            store.StoreId, fromPhone, cancellationToken);

        Core.Entities.Thread thread = await _threadRepo.FindOrCreateAsync(
            store.StoreId, identityId, customer.CustomerId, cancellationToken);

        if (thread.CustomerId is null || thread.CustomerId == 0)
        {
            await _threadRepo.UpdateCustomerIdAsync(thread.ThreadId, customer.CustomerId, cancellationToken);
        }

        // 7. Reopen thread if closed
        if (thread.Status == "Closed")
        {
            // Thread will be reopened implicitly by the LastMessageAt update
            thread.Status = "Open";
        }

        // 8. Parse media attachments
        string? mediaJson = null;
        if (numMedia > 0)
        {
            List<object> mediaList = new();
            for (int i = 0; i < numMedia; i++)
            {
                string? mediaUrl = form[$"MediaUrl{i}"];
                string? contentType = form[$"MediaContentType{i}"];
                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    mediaList.Add(new { url = mediaUrl, content_type = contentType });
                }
            }
            if (mediaList.Count > 0)
            {
                mediaJson = System.Text.Json.JsonSerializer.Serialize(mediaList);
            }
        }

        // 9. Classify and create inbound message
        string category = MessageClassifier.Classify(body);

        Message message = await _messageRepo.CreateInboundAsync(
            store.StoreId, thread.ThreadId, storePhoneE164,
            fromPhone, toPhone, body, mediaJson, category,
            cancellationToken);

        // Set TwilioSid on the inbound message (interface does not accept SID at creation)
        await _messageRepo.UpdateSentAsync(message.MessageId, messageSid, "Received", cancellationToken);

        // 10. Bump thread timestamp + increment unread
        await _threadRepo.UpdateLastMessageAtAsync(thread.ThreadId, message.CreatedAt, cancellationToken);
        await _threadRepo.IncrementUnreadAsync(thread.ThreadId, cancellationToken);

        // 11. Realtime push
        MessageDto messageDto = new MessageDto
        {
            MessageId = message.MessageId,
            ThreadId = thread.ThreadId,
            Direction = "Inbound",
            FromE164 = fromPhone,
            ToE164 = toPhone,
            Body = body,
            Status = "Received",
            TwilioSid = messageSid,
            MediaJson = mediaJson,
            Category = category,
            CreatedAt = message.CreatedAt
        };

        ThreadDto threadDto = new ThreadDto
        {
            ThreadId = thread.ThreadId,
            StoreId = store.StoreId,
            LastMessageAt = message.CreatedAt,
            UnreadCount = thread.UnreadCount + 1,
            Status = "Open"
        };

        await _realtimeService.PushMessageNewAsync(
            store.StoreId, thread.ThreadId, messageDto, threadDto, cancellationToken);

        // 12. Return empty TwiML to acknowledge receipt
        return TwimlOk();
    }

    // Returns an empty TwiML response (200 OK with XML content).
    private ContentResult TwimlOk()
    {
        return Content("<Response></Response>", "application/xml");
    }
}
