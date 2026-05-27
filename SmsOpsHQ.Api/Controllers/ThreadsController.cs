using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ThreadsController : ControllerBase
{
    private readonly IThreadRepository _threadRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreEventBus? _eventBus;
    private readonly ILogger<ThreadsController> _logger;

    public ThreadsController(
        IThreadRepository threadRepo,
        IMessageRepository messageRepo,
        ICustomerRepository customerRepo,
        IStoreRepository storeRepo,
        ILogger<ThreadsController> logger,
        IStoreEventBus? eventBus = null)
    {
        _threadRepo = threadRepo;
        _messageRepo = messageRepo;
        _customerRepo = customerRepo;
        _storeRepo = storeRepo;
        _eventBus = eventBus;
        _logger = logger;
    }

    // GET /api/inbox?store_id=1&filter=open&search=john&twilio_number_id=1
    // Returns thread list sorted by last message time with customer info.
    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox(
        [FromQuery(Name = "store_id")] int storeId,
        [FromQuery] string filter = "open",
        [FromQuery] string? search = null,
        [FromQuery(Name = "twilio_number_id")] int? twilioNumberId = null,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        // If no twilio_number_id specified, use default for the store
        if (twilioNumberId is null)
        {
            Store? store = await _storeRepo.GetByIdAsync(storeId, cancellationToken);
            if (store is not null && store.DefaultNumberId > 0)
                twilioNumberId = store.DefaultNumberId;
        }

        // Single query with customer data (avoids N+1)
        List<(Core.Entities.Thread thread, Customer? customer)> inboxRows = await _threadRepo.GetInboxWithCustomersAsync(
            storeId, filter, search, twilioNumberId, cancellationToken);

        // Build response with customer data and last message
        List<object> result = new(inboxRows.Count);
        foreach (var (t, customer) in inboxRows)
        {
            CustomerDto? customerDto = null;
            if (customer is not null)
            {
                customerDto = new CustomerDto
                {
                    CustomerId = customer.CustomerId,
                    PhoneE164 = customer.PhoneE164,
                    FirstName = customer.FirstName,
                    LastName = customer.LastName
                };
            }

            // Load last message
            Message? lastMsg = await _messageRepo.GetLastMessageAsync(t.ThreadId, cancellationToken);

            object? lastMessageData = lastMsg is not null ? new
            {
                message_id = lastMsg.MessageId,
                direction = lastMsg.Direction,
                body = lastMsg.Body,
                created_at = lastMsg.CreatedAt.ToString("o"),
                status = lastMsg.Status,
                from_phone = lastMsg.FromE164,
                to_phone = lastMsg.ToE164,
                media_json = lastMsg.MediaJson
            } : null;

            result.Add(new
            {
                thread_id = t.ThreadId,
                customer = customerDto is not null ? new
                {
                    id = customerDto.CustomerId,
                    phone = customerDto.PhoneE164,
                    name = $"{customerDto.FirstName ?? ""} {customerDto.LastName ?? ""}".Trim(),
                    first_name = customerDto.FirstName ?? "",
                    last_name = customerDto.LastName ?? ""
                } : null,
                last_message = lastMessageData,
                last_message_at = t.LastMessageAt?.ToString("o"),
                unread_count = t.UnreadCount,
                status = t.Status
            });
        }

        return Ok(result);
    }

    // GET /api/thread/{threadId}?store_id=1&include_xpd=true
    // Thread details with messages and optional XPD customer summary.
    [HttpGet("thread/{threadId}")]
    public async Task<IActionResult> GetThreadDetails(
        int threadId,
        [FromQuery(Name = "store_id")] int storeId,
        [FromQuery(Name = "include_xpd")] bool includeXpd = false,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        Core.Entities.Thread? thread = await _threadRepo.GetByIdAsync(storeId, threadId, cancellationToken);
        if (thread is null)
            return Problem(statusCode: 404, detail: "Thread not found");

        if (thread.UnreadCount > 0)
        {
            await _threadRepo.MarkReadAsync(threadId, cancellationToken);
            // Drives the Hub's "unread count" tile back down within ~2s instead
            // of waiting for the next periodic heartbeat.
            _eventBus?.NotifyActivity("thread.read");
        }

        // Load messages (up to 200)
        List<Message> messages = await _messageRepo.GetByThreadAsync(storeId, threadId, 200, cancellationToken);

        // Load customer data
        CustomerDto? customerDto = null;
        if (thread.CustomerId is not null && thread.CustomerId > 0)
        {
            Customer? customer = await _customerRepo.GetByIdAsync(storeId, thread.CustomerId.Value, cancellationToken);
            if (customer is not null)
            {
                customerDto = new CustomerDto
                {
                    CustomerId = customer.CustomerId,
                    PhoneE164 = customer.PhoneE164,
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Notes = customer.Notes
                };
            }
        }

        // Build messages list
        List<object> messagesList = new(messages.Count);
        foreach (Message m in messages)
        {
            messagesList.Add(new
            {
                id = m.MessageId,
                direction = m.Direction,
                from_phone = m.FromE164,
                to_phone = m.ToE164,
                body = m.Body,
                category = m.Category,
                created_at = m.CreatedAt.ToString("o"),
                status = m.Status,
                is_outbound = m.Direction == "Outbound",
                media_json = m.MediaJson
            });
        }

        object response = new
        {
            thread = new
            {
                id = thread.ThreadId,
                thread_id = thread.ThreadId,
                status = thread.Status,
                twilio_number_id = thread.TwilioNumberId,
                customer = customerDto is not null ? new
                {
                    id = customerDto.CustomerId,
                    phone = customerDto.PhoneE164,
                    name = $"{customerDto.FirstName ?? ""} {customerDto.LastName ?? ""}".Trim(),
                    first_name = customerDto.FirstName,
                    last_name = customerDto.LastName,
                    notes = customerDto.Notes
                } : null
            },
            messages = messagesList
        };

        return Ok(response);
    }

    // DELETE /api/thread/{threadId}?store_id=1
    // Deletes a single thread and its messages (cascade).
    [HttpDelete("thread/{threadId}")]
    public async Task<IActionResult> DeleteThread(
        int threadId,
        [FromQuery(Name = "store_id")] int storeId,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        Core.Entities.Thread? thread = await _threadRepo.GetByIdAsync(storeId, threadId, cancellationToken);
        if (thread is null)
            return Problem(statusCode: 404, detail: "Thread not found");

        await _threadRepo.DeleteAsync(storeId, threadId, cancellationToken);

        return Ok(new { status = "deleted", thread_id = threadId });
    }

    // DELETE /api/conversations?store_id=1
    // Deletes all threads and messages for a store.
    [HttpDelete("conversations")]
    public async Task<IActionResult> DeleteAllConversations(
        [FromQuery(Name = "store_id")] int storeId,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized");

        await _threadRepo.DeleteAllAsync(storeId, cancellationToken);

        return Ok(new { status = "deleted" });
    }

    // GET /api/threads/bulk?store_id=1&thread_ids=450,451,463,449
    // Bulk-loads multiple threads with their messages in one request.
    [HttpGet("threads/bulk")]
    public async Task<IActionResult> GetThreadsBulk(
        [FromQuery(Name = "store_id")] int storeId,
        [FromQuery(Name = "thread_ids")] string threadIds,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        // Parse comma-separated thread IDs
        List<int> threadIdList;
        try
        {
            threadIdList = threadIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToList();
        }
        catch (FormatException)
        {
            return Problem(statusCode: 400, detail: "Invalid thread_ids format. Use comma-separated integers.");
        }

        if (threadIdList.Count == 0)
            return Problem(statusCode: 400, detail: "No thread_ids provided");

        if (threadIdList.Count > 200)
            return Problem(statusCode: 400, detail: "Maximum 200 threads per request");

        // Load threads
        List<Core.Entities.Thread> threads = await _threadRepo.GetByIdsAsync(
            storeId, threadIdList, cancellationToken);

        // Load all messages for matched threads
        Dictionary<int, List<object>> messagesByThread = new();
        foreach (Core.Entities.Thread t in threads)
        {
            List<Message> threadMessages = await _messageRepo.GetByThreadAsync(
                storeId, t.ThreadId, 500, cancellationToken);

            messagesByThread[t.ThreadId] = threadMessages.Select(m => (object)new
            {
                id = m.MessageId,
                direction = m.Direction,
                body = m.Body,
                category = m.Category,
                created_at = m.CreatedAt.ToString("o"),
                status = m.Status,
                is_outbound = m.Direction == "Outbound",
                media_json = m.MediaJson
            }).ToList();
        }

        // Build response keyed by thread ID
        Dictionary<string, object> threadsResult = new();
        foreach (Core.Entities.Thread t in threads)
        {
            // Load customer data
            object? customerData = null;
            if (t.CustomerId is not null && t.CustomerId > 0)
            {
                Customer? customer = await _customerRepo.GetByIdAsync(storeId, t.CustomerId.Value, cancellationToken);
                if (customer is not null)
                {
                    customerData = new
                    {
                        id = customer.CustomerId,
                        phone = customer.PhoneE164,
                        name = $"{customer.FirstName ?? ""} {customer.LastName ?? ""}".Trim(),
                        first_name = customer.FirstName ?? "",
                        last_name = customer.LastName ?? "",
                        notes = customer.Notes
                    };
                }
            }

            threadsResult[t.ThreadId.ToString()] = new
            {
                thread = new
                {
                    id = t.ThreadId,
                    status = t.Status,
                    twilio_number_id = t.TwilioNumberId,
                    customer = customerData
                },
                messages = messagesByThread.GetValueOrDefault(t.ThreadId, new List<object>())
            };
        }

        return Ok(new
        {
            requested = threadIdList.Count,
            found = threadsResult.Count,
            threads = threadsResult
        });
    }

    // GET /api/messages/bulk?store_id=1
    // Bulk-loads all messages for all threads in a store, grouped by thread_id.
    [HttpGet("messages/bulk")]
    public async Task<IActionResult> GetAllMessagesBulk(
        [FromQuery(Name = "store_id")] int storeId,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        List<Message> allMessages = await _messageRepo.GetAllByStoreAsync(storeId, cancellationToken);

        // Group by thread_id
        Dictionary<int, List<object>> messagesByThread = new();
        foreach (Message m in allMessages)
        {
            if (!messagesByThread.ContainsKey(m.ThreadId))
                messagesByThread[m.ThreadId] = new List<object>();

            messagesByThread[m.ThreadId].Add(new
            {
                id = m.MessageId,
                direction = m.Direction,
                body = m.Body,
                category = m.Category,
                created_at = m.CreatedAt.ToString("o"),
                status = m.Status,
                is_outbound = m.Direction == "Outbound",
                media_json = m.MediaJson
            });
        }

        return Ok(new
        {
            thread_count = messagesByThread.Count,
            message_count = allMessages.Count,
            messages_by_thread = messagesByThread
        });
    }
}
