using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class MessagesController : ControllerBase
{
    private readonly IMessageRepository _messageRepo;
    private readonly IThreadRepository _threadRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IOptOutRepository _optOutRepo;
    private readonly IOutboundNumberResolver _outboundNumberResolver;
    private readonly IIdentityResolver _identityResolver;
    private readonly ITwilioService _twilioService;
    private readonly IRealtimeService _realtimeService;
    private readonly IStoreEventBus? _eventBus;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(
        IMessageRepository messageRepo,
        IThreadRepository threadRepo,
        ICustomerRepository customerRepo,
        IOptOutRepository optOutRepo,
        IOutboundNumberResolver outboundNumberResolver,
        IIdentityResolver identityResolver,
        ITwilioService twilioService,
        IRealtimeService realtimeService,
        ILogger<MessagesController> logger,
        IStoreEventBus? eventBus = null)
    {
        _messageRepo = messageRepo;
        _threadRepo = threadRepo;
        _customerRepo = customerRepo;
        _optOutRepo = optOutRepo;
        _outboundNumberResolver = outboundNumberResolver;
        _identityResolver = identityResolver;
        _twilioService = twilioService;
        _realtimeService = realtimeService;
        _eventBus = eventBus;
        _logger = logger;
    }

    // POST /api/send
    // Full outbound SMS pipeline: auth, opt-out check, identity resolve,
    // thread find/create, classify, create message, bump thread, Twilio send,
    // update status, push realtime.
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Store authorization
        if (!User.CanAccessStore(request.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        int userId = User.GetUserId();

        string? normalizedTo = PhoneUtils.NormalizeToE164(request.ToPhone);
        if (normalizedTo is null)
            return Problem(statusCode: 400, detail: "Destination phone must be a valid US phone number.");

        // 2. Opt-out check
        bool isOptedOut = await _optOutRepo.ExistsAsync(request.StoreId, normalizedTo, cancellationToken);
        if (isOptedOut)
            return Problem(statusCode: 400, detail: "Customer is opted out.");

        // 3. Resolve the exact requested sender. An explicit invalid selection
        // never silently falls back to the store default.
        OutboundNumberResolution sender;
        try
        {
            sender = await _outboundNumberResolver.ResolveAsync(
                request.StoreId, request.TwilioNumberId, cancellationToken);
        }
        catch (OutboundNumberValidationException ex)
        {
            return Problem(
                title: "Invalid sender number",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message);
        }

        string fromNumber = sender.PhoneE164;

        // 4. Resolve or load thread
        Core.Entities.Thread thread;
        if (request.ThreadId is not null)
        {
            Core.Entities.Thread? existingThread = await _threadRepo.GetByIdAsync(
                request.StoreId, request.ThreadId.Value, cancellationToken);
            if (existingThread is null)
                return Problem(statusCode: 404, detail: "Thread not found");

            if (!string.Equals(existingThread.ContactPhoneE164, normalizedTo, StringComparison.Ordinal))
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conversation phone mismatch",
                    detail: "The destination phone does not match this conversation.");
            }

            if (existingThread.TwilioNumberId != sender.TwilioNumberId)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conversation sender mismatch",
                    detail: "The selected store number does not match this conversation.");
            }

            thread = existingThread;
        }
        else
        {
            // Try to resolve identity from phone index (XPD-synced CustomerPhones).
            // If the phone is not in the database, identityId will be null — that's fine;
            // we still create a thread and customer so SMS can be sent to anyone.
            int? identityId = await _identityResolver.ResolveIdentityIdAsync(
                request.StoreId, normalizedTo, cancellationToken);

            if (identityId is null)
            {
                _logger.LogInformation(
                    "Phone {Phone} not found in XPD for store {StoreId}; sending to unknown contact",
                    normalizedTo, request.StoreId);
            }

            Customer customer = await _customerRepo.FindOrCreateAsync(
                request.StoreId, normalizedTo, cancellationToken);

            thread = await _threadRepo.FindOrCreateAsync(
                request.StoreId,
                sender.TwilioNumberId,
                normalizedTo,
                identityId,
                customer.CustomerId,
                cancellationToken);

            if (thread.CustomerId is null || thread.CustomerId == 0)
            {
                await _threadRepo.UpdateCustomerIdAsync(thread.ThreadId, customer.CustomerId, cancellationToken);
            }
        }

        // 5. Classify and create outbound message (Status = Queued)
        string category = MessageClassifier.Classify(request.Body);

        Message message = await _messageRepo.CreateOutboundAsync(
            request.StoreId, thread.ThreadId, fromNumber,
            fromNumber, normalizedTo, request.Body,
            null, category, userId,
            cancellationToken);

        // 6. Bump thread timestamp BEFORE Twilio call
        await _threadRepo.UpdateLastMessageAtAsync(thread.ThreadId, message.CreatedAt, cancellationToken);

        // 7. Call Twilio
        TwilioSendResult twilioResult = await _twilioService.SendSmsAsync(
            fromNumber, normalizedTo, request.Body,
            request.MediaUrls, null, cancellationToken);

        // 8. Update message status based on Twilio result.
        // Mock mode is treated as a distinct, visible status so the operator
        // can tell at a glance that the message was NOT actually delivered.
        string finalStatus = twilioResult.IsMock
            ? "Mock"
            : (twilioResult.Success ? "Sent" : "Failed");

        if (twilioResult.IsMock)
        {
            _logger.LogWarning(
                "Outbound SMS recorded as MOCK (message {MessageId}, thread {ThreadId}, store {StoreId}). " +
                "Twilio credentials are not configured; the customer will NOT receive this message.",
                message.MessageId, thread.ThreadId, request.StoreId);
        }

        if (twilioResult.TwilioSid is not null)
        {
            await _messageRepo.UpdateSentAsync(
                message.MessageId, twilioResult.TwilioSid, finalStatus, cancellationToken);
        }

        // 9. Realtime push
        MessageDto messageDto = new MessageDto
        {
            MessageId = message.MessageId,
            ThreadId = thread.ThreadId,
            Direction = "Outbound",
            FromE164 = fromNumber,
            ToE164 = normalizedTo,
            Body = request.Body,
            Category = category,
            Status = finalStatus,
            TwilioSid = twilioResult.TwilioSid,
            CreatedAt = message.CreatedAt
        };

        ThreadDto threadDto = new ThreadDto
        {
            ThreadId = thread.ThreadId,
            StoreId = request.StoreId,
            TwilioNumberId = thread.TwilioNumberId,
            ContactPhoneE164 = thread.ContactPhoneE164,
            Status = thread.Status,
            LastMessageAt = message.CreatedAt,
            UnreadCount = thread.UnreadCount
        };

        await _realtimeService.PushMessageNewAsync(
            request.StoreId, thread.ThreadId, messageDto, threadDto, cancellationToken);

        // Real-time Hub update: wake the heartbeat pusher so the dashboard's
        // "messages sent today" counter refreshes within ~2s.
        _eventBus?.NotifyActivity("sms.sent");

        return Ok(new
        {
            status = "ok",
            message_id = message.MessageId,
            twilio_number_id = sender.TwilioNumberId,
            category,
            mock = twilioResult.IsMock,
            twilio = new
            {
                sid = twilioResult.TwilioSid,
                status = twilioResult.Status,
                error = twilioResult.ErrorMessage,
                mock = twilioResult.IsMock
            }
        });
    }

    // POST /api/thread/{threadId}/notes
    // Creates an internal note on a thread. Does not bump the thread timestamp.
    [HttpPost("thread/{threadId}/notes")]
    public async Task<IActionResult> CreateNote(
        int threadId,
        [FromBody] CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(request.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized");

        Core.Entities.Thread? thread = await _threadRepo.GetByIdAsync(
            request.StoreId, threadId, cancellationToken);
        if (thread is null)
            return Problem(statusCode: 404, detail: "Thread not found");

        int userId = User.GetUserId();

        Message note = await _messageRepo.CreateNoteAsync(
            request.StoreId, thread.ThreadId, request.Content, userId, cancellationToken);

        // Realtime push (note appears instantly for other connected users)
        MessageDto noteDto = new MessageDto
        {
            MessageId = note.MessageId,
            ThreadId = thread.ThreadId,
            Direction = "Note",
            FromE164 = "System",
            ToE164 = "System",
            Body = request.Content,
            Status = "Internal",
            CreatedAt = note.CreatedAt
        };

        ThreadDto emptyThreadDto = new ThreadDto
        {
            ThreadId = thread.ThreadId,
            StoreId = request.StoreId,
            TwilioNumberId = thread.TwilioNumberId,
            ContactPhoneE164 = thread.ContactPhoneE164
        };

        await _realtimeService.PushMessageNewAsync(
            request.StoreId, thread.ThreadId, noteDto, emptyThreadDto, cancellationToken);

        // Notes don't change SMS counters but they do bump "last activity",
        // and the operator added them moments ago -- worth surfacing fast.
        _eventBus?.NotifyActivity("note.created");

        return Ok(new { status = "ok", message_id = note.MessageId });
    }

    // GET /api/messages?store_id=1&category=reminder&thread_id=7&limit=100&offset=0
    // Lists messages with optional category and thread filters, with pagination.
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(
        [FromQuery(Name = "store_id")] int storeId,
        [FromQuery] string? category = null,
        [FromQuery(Name = "thread_id")] int? threadId = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        // Validate category
        List<string> validCategories = MessageClassifier.GetValidCategories();
        if (!string.IsNullOrWhiteSpace(category) && category != "all"
            && !validCategories.Contains(category))
        {
            return Problem(statusCode: 400,
                detail: $"Invalid category. Must be one of: {string.Join(", ", validCategories)}, or 'all'");
        }

        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);

        (List<Message> messages, int totalCount) = await _messageRepo.GetPagedAsync(
            storeId, category, threadId, limit, offset, cancellationToken);

        List<object> messagesList = new(messages.Count);
        foreach (Message m in messages)
        {
            messagesList.Add(new
            {
                id = m.MessageId,
                thread_id = m.ThreadId,
                direction = m.Direction,
                from_phone = m.FromE164,
                to_phone = m.ToE164,
                body = m.Body,
                category = m.Category,
                category_display = MessageClassifier.GetDisplayName(m.Category),
                status = m.Status,
                created_at = m.CreatedAt.ToString("o"),
                media_json = m.MediaJson
            });
        }

        return Ok(new
        {
            total = totalCount,
            limit,
            offset,
            category = category ?? "all",
            messages = messagesList
        });
    }

    // GET /api/messages/counts?store_id=1&thread_id=7
    // Returns message counts per category for badge display.
    [HttpGet("messages/counts")]
    public async Task<IActionResult> GetMessageCounts(
        [FromQuery(Name = "store_id")] int storeId,
        [FromQuery(Name = "thread_id")] int? threadId = null,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        Dictionary<string, int> counts = await _messageRepo.GetCountsByCategoryAsync(
            storeId, threadId, cancellationToken);

        int allCount = counts.Values.Sum();

        return Ok(new
        {
            store_id = storeId,
            thread_id = threadId,
            counts = new
            {
                all = allCount,
                reminder = counts.GetValueOrDefault("reminder"),
                directions = counts.GetValueOrDefault("directions"),
                promotions = counts.GetValueOrDefault("promotions"),
                general = counts.GetValueOrDefault("general")
            },
            categories = new[]
            {
                new { code = "all", name = "All Messages", count = allCount },
                new { code = "reminder", name = "Reminders", count = counts.GetValueOrDefault("reminder") },
                new { code = "directions", name = "Directions", count = counts.GetValueOrDefault("directions") },
                new { code = "promotions", name = "Promotions", count = counts.GetValueOrDefault("promotions") },
                new { code = "general", name = "General", count = counts.GetValueOrDefault("general") }
            }
        });
    }

    // GET /api/messages/categories
    // Returns static category metadata (code, name, description).
    [HttpGet("messages/categories")]
    public IActionResult GetCategories()
    {
        return Ok(new
        {
            categories = new[]
            {
                new { code = "all",        name = "All Messages", description = "All messages regardless of category" },
                new { code = "reminder",   name = "Reminders",    description = "Messages containing ticket numbers and payment reminders" },
                new { code = "directions", name = "Directions",   description = "Messages with location information and map links" },
                new { code = "promotions", name = "Promotions",   description = "Marketing and promotional messages" },
                new { code = "general",    name = "General",      description = "Regular customer communications" }
            }
        });
    }
}
