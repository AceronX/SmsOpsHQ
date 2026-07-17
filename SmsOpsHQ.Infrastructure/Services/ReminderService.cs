using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// Sends, tracks, and queries pawn SMS reminders.
// Ported from Python reminder_service.py / main.py.
public sealed class ReminderService : IReminderService
{
    private readonly AppDbContext _db;
    private readonly ITwilioService _twilioService;
    private readonly IStorePhoneResolver _storePhoneResolver;
    private readonly ILogger<ReminderService> _logger;
    private readonly IThreadRepository? _threadRepo;
    private readonly IMessageRepository? _messageRepo;

    private static readonly int[] ReminderIntervals = { -7, 0, 7, 14, 30 };

    private static readonly Dictionary<int, string> ReminderDescriptions = new()
    {
        { -7, "7 Days Before Expiration" },
        {  0, "Expiration Day" },
        {  7, "7 Days After Expiration" },
        { 14, "14 Days After Expiration" },
        { 30, "30 Days After Expiration (Final Notice)" }
    };

    private readonly bool _testMode;
    private readonly int _sendDelayMs;
    private readonly int _batchSize;
    private readonly int _batchPauseMs;

    public ReminderService(
        AppDbContext db,
        ITwilioService twilioService,
        IStorePhoneResolver storePhoneResolver,
        ILogger<ReminderService> logger,
        IConfiguration? configuration = null,
        IThreadRepository? threadRepo = null,
        IMessageRepository? messageRepo = null)
    {
        _db = db;
        _twilioService = twilioService;
        _storePhoneResolver = storePhoneResolver;
        _logger = logger;
        _threadRepo = threadRepo;
        _messageRepo = messageRepo;
        _testMode = configuration?.GetValue("Reminders:TestMode", false) ?? false;
        _sendDelayMs = configuration?.GetValue("Reminders:SendDelayMs", 2000) ?? 2000;
        _batchSize = configuration?.GetValue("Reminders:BatchSize", 10) ?? 10;
        _batchPauseMs = configuration?.GetValue("Reminders:BatchPauseMs", 30000) ?? 30000;
    }

    // ── Send Single Reminder ─────────────────────────────────────────

    public async Task<ReminderSendResult> SendReminderAsync(
        SendReminderRequest request, CancellationToken cancellationToken = default)
    {
        string reminderType = $"reminder_{request.DaysDiff}";

        if (await IsPhoneExcludedAsync(request.Phone, cancellationToken))
        {
            return new ReminderSendResult
            {
                Success = false,
                Message = "Phone number is excluded or unsubscribed"
            };
        }

        if (request.DaysDiff == 30)
        {
            bool finalAttempted = await _db.SmsReminders.AsNoTracking()
                .AnyAsync(r => r.TicketKey == request.TicketKey
                            && r.DueDate == request.DueDate
                            && r.ReminderType == reminderType,
                    cancellationToken);
            if (finalAttempted)
            {
                return new ReminderSendResult
                {
                    Success = false,
                    Message = $"Final reminder {reminderType} already attempted for this ticket"
                };
            }
        }
        else if (await WasReminderSentAsync(request.TicketKey, request.DueDate, reminderType, cancellationToken))
        {
            return new ReminderSendResult
            {
                Success = false,
                Message = $"Reminder {reminderType} already sent for this ticket"
            };
        }

        DateTime dueDateTime = ParseDueDate(request.DueDate);
        string? fromE164 = await _storePhoneResolver.GetStorePhoneAsync(request.StoreId, cancellationToken);
        fromE164 ??= "+13479527212"; // fallback

        string storePhoneDisplay = FormatPhoneDisplay(fromE164);
        string? messageBody = GetMessageTemplate(request.DaysDiff, request.TransNo, dueDateTime, storePhoneDisplay);

        if (messageBody is null)
        {
            return new ReminderSendResult
            {
                Success = false,
                Message = $"No template for {request.DaysDiff} days"
            };
        }

        string? toE164 = PhoneUtils.NormalizeToE164(request.Phone);
        if (toE164 is null)
        {
            await AddToExcludedAsync(request.Phone, "Invalid number - failed validation", null, cancellationToken);
            _logger.LogInformation("Auto-excluded invalid phone {Phone}", request.Phone);
            return new ReminderSendResult
            {
                Success = false,
                Message = "Invalid phone number format (auto-excluded)"
            };
        }

        if (_testMode)
        {
            _logger.LogInformation(
                "TEST MODE reminder: To={To} Type={Type} Body={Body}",
                toE164, reminderType, messageBody.Length > 100 ? messageBody[..100] + "..." : messageBody);

            string testSid = $"TEST_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            await LogReminderAsync(
                request.TicketKey, request.CustomerKey, request.DueDate,
                toE164, reminderType, messageBody, true, testSid, null,
                request.UserId, request.StoreId, fromE164, cancellationToken);

            await AddToConversationThreadAsync(
                request.StoreId, fromE164, toE164, messageBody, null, cancellationToken);

            return new ReminderSendResult
            {
                Success = true,
                Message = "TEST MODE - Reminder logged but not sent",
                TwilioSid = testSid,
                ReminderType = reminderType,
                TestMode = true
            };
        }

        var twilioResult = await _twilioService.SendSmsAsync(fromE164, toE164, messageBody,
            cancellationToken: cancellationToken);

        bool success = twilioResult.Success;

        await LogReminderAsync(
            request.TicketKey, request.CustomerKey, request.DueDate,
            toE164, reminderType, messageBody, success, twilioResult.TwilioSid,
            twilioResult.ErrorMessage, request.UserId, request.StoreId, fromE164, cancellationToken);

        if (success)
        {
            await AddToConversationThreadAsync(
                request.StoreId, fromE164, toE164, messageBody,
                twilioResult.TwilioSid, cancellationToken);
        }

        return new ReminderSendResult
        {
            Success = success,
            Message = success ? "Reminder sent successfully" : (twilioResult.ErrorMessage ?? "Failed to send"),
            TwilioSid = twilioResult.TwilioSid,
            ReminderType = reminderType
        };
    }

    // ── Send Combined Reminder ───────────────────────────────────────

    public async Task<ReminderSendResult> SendCombinedReminderAsync(
        SendCombinedReminderRequest request, CancellationToken cancellationToken = default)
    {
        if (await IsPhoneExcludedAsync(request.Phone, cancellationToken))
        {
            return new ReminderSendResult
            {
                Success = false,
                Message = "Phone number is excluded or unsubscribed"
            };
        }

        string? fromE164 = await _storePhoneResolver.GetStorePhoneAsync(request.StoreId, cancellationToken);
        fromE164 ??= "+13479527212";
        string storePhoneDisplay = FormatPhoneDisplay(fromE164);

        string messageBody = BuildCombinedMessage(request.Tickets, storePhoneDisplay);

        string? toE164 = PhoneUtils.NormalizeToE164(request.Phone);
        if (toE164 is null)
        {
            await AddToExcludedAsync(request.Phone, "Invalid number - failed validation", null, cancellationToken);
            _logger.LogInformation("Auto-excluded invalid phone {Phone}", request.Phone);
            return new ReminderSendResult
            {
                Success = false,
                Message = "Invalid phone number format (auto-excluded)"
            };
        }

        if (_testMode)
        {
            _logger.LogInformation(
                "TEST MODE combined reminder: To={To} Tickets={Count}",
                toE164, request.Tickets.Count);

            string testSid = $"TEST_COMBINED_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            foreach (CombinedTicketInfo ticket in request.Tickets)
            {
                string combinedType = $"combined_{ticket.NextReminder.DaysDiff}";
                await LogReminderAsync(
                    ticket.TicketKey, request.CustomerKey, ticket.DueDateStr,
                    toE164, combinedType, messageBody, true, testSid, null,
                    request.UserId, request.StoreId, fromE164, cancellationToken);
            }

            await AddToConversationThreadAsync(
                request.StoreId, fromE164, toE164, messageBody, null, cancellationToken);

            return new ReminderSendResult
            {
                Success = true,
                Message = $"TEST MODE - Combined reminder for {request.Tickets.Count} tickets logged",
                TwilioSid = testSid,
                ReminderType = "combined",
                TestMode = true,
                TicketCount = request.Tickets.Count
            };
        }

        var twilioResult = await _twilioService.SendSmsAsync(fromE164, toE164, messageBody,
            cancellationToken: cancellationToken);

        bool success = twilioResult.Success;

        foreach (CombinedTicketInfo ticket in request.Tickets)
        {
            string combinedType = $"combined_{ticket.NextReminder.DaysDiff}";
            await LogReminderAsync(
                ticket.TicketKey, request.CustomerKey, ticket.DueDateStr,
                toE164, combinedType, messageBody, success, twilioResult.TwilioSid,
                twilioResult.ErrorMessage, request.UserId, request.StoreId, fromE164, cancellationToken);
        }

        if (success)
        {
            await AddToConversationThreadAsync(
                request.StoreId, fromE164, toE164, messageBody,
                twilioResult.TwilioSid, cancellationToken);
        }

        return new ReminderSendResult
        {
            Success = success,
            Message = success
                ? $"Combined reminder for {request.Tickets.Count} tickets sent"
                : (twilioResult.ErrorMessage ?? "Failed to send"),
            TwilioSid = twilioResult.TwilioSid,
            ReminderType = "combined",
            TicketCount = request.Tickets.Count
        };
    }

    // ── Batch Processing ─────────────────────────────────────────────

    public async Task<BatchReminderResult> RunBatchRemindersAsync(
        int storeId, int maxCount, int? userId, CancellationToken cancellationToken = default)
    {
        DateTime today = DateTime.Now.Date;
        BatchReminderResult results = new();

        // Load active tickets with customer phone info. Request extra rows to account for skips.
        int[] allowedTypes = { 3, 4, 5 };
        var tickets = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.Active == 1
                         && t.Type != null && allowedTypes.Contains(t.Type.Value)
                         && t.DueDate != null && t.DueDate != "")
            .Join(_db.Customers.AsNoTracking(),
                t => t.CustomerKey,
                c => c.CustomerKey,
                (t, c) => new
                {
                    t.Key,
                    t.CustomerKey,
                    t.TransNo,
                    t.DueDate,
                    Phone = c.ResPhone ?? c.BusPhone,
                    c.FirstName,
                    c.LastName
                })
            .Where(x => x.Phone != null)
            .OrderBy(x => x.CustomerKey)
            .ThenBy(x => x.DueDate)
            .Take(maxCount * 3)
            .ToListAsync(cancellationToken);

        // Group by normalized phone to combine reminders per customer.
        Dictionary<string, CustomerTicketGroup> customerTickets = new();

        foreach (var ticket in tickets)
        {
            string? phoneNormalized = PhoneUtils.ExtractLast10Digits(ticket.Phone);
            if (phoneNormalized is null)
            {
                results.SkippedCount++;
                continue;
            }

            DateTime? dueDate = ParseDueDateNullable(ticket.DueDate);
            if (dueDate is null)
            {
                results.SkippedCount++;
                continue;
            }

            int daysLate = (today - dueDate.Value).Days;

            NextReminderResult? nextReminder = await GetNextReminderTypeAsync(
                ticket.Key, ticket.DueDate!, daysLate, cancellationToken);

            if (nextReminder is null)
            {
                results.SkippedCount++;
                continue;
            }

            if (!customerTickets.TryGetValue(phoneNormalized, out CustomerTicketGroup? group))
            {
                group = new CustomerTicketGroup
                {
                    Phone = ticket.Phone!,
                    CustomerKey = ticket.CustomerKey,
                    FirstName = ticket.FirstName,
                    LastName = ticket.LastName
                };
                customerTickets[phoneNormalized] = group;
            }

            group.Tickets.Add(new CombinedTicketInfo
            {
                TicketKey = ticket.Key,
                TransNo = ticket.TransNo?.ToString() ?? "",
                DueDateStr = ticket.DueDate!,
                DaysLate = daysLate,
                NextReminder = nextReminder
            });
        }

        int sendCountInBatch = 0;

        foreach (CustomerTicketGroup group in customerTickets.Values)
        {
            if (results.SentCount >= maxCount)
                break;

            bool sent;

            if (group.Tickets.Count == 1)
            {
                CombinedTicketInfo t = group.Tickets[0];
                ReminderSendResult result = await SendReminderAsync(new SendReminderRequest
                {
                    TicketKey = t.TicketKey,
                    CustomerKey = group.CustomerKey,
                    Phone = group.Phone,
                    TransNo = t.TransNo,
                    DueDate = t.DueDateStr,
                    DaysDiff = t.NextReminder.DaysDiff,
                    UserId = userId,
                    StoreId = storeId
                }, cancellationToken);

                sent = result.Success;
                if (sent) results.SentCount++; else results.FailedCount++;

                results.Details.Add(new BatchReminderDetail
                {
                    TicketKey = t.TicketKey,
                    TransNo = t.TransNo,
                    Phone = group.Phone,
                    ReminderType = t.NextReminder.ReminderType,
                    Success = result.Success,
                    Message = result.Message
                });
            }
            else
            {
                ReminderSendResult result = await SendCombinedReminderAsync(new SendCombinedReminderRequest
                {
                    CustomerKey = group.CustomerKey,
                    Phone = group.Phone,
                    Tickets = group.Tickets,
                    UserId = userId,
                    StoreId = storeId
                }, cancellationToken);

                sent = result.Success;
                if (sent)
                {
                    results.SentCount++;
                    results.CombinedCount += group.Tickets.Count;
                }
                else
                {
                    results.FailedCount++;
                }

                results.Details.Add(new BatchReminderDetail
                {
                    TicketKeys = group.Tickets.Select(x => x.TicketKey).ToList(),
                    TransNos = group.Tickets.Select(x => x.TransNo).ToList(),
                    Phone = group.Phone,
                    ReminderType = "combined",
                    TicketCount = group.Tickets.Count,
                    Success = result.Success,
                    Message = result.Message
                });
            }

            if (sent)
            {
                sendCountInBatch++;
                if (_batchSize > 0 && sendCountInBatch % _batchSize == 0)
                {
                    _logger.LogInformation(
                        "Batch throttle pause: {Count} sent, waiting {PauseMs}ms",
                        sendCountInBatch, _batchPauseMs);
                    await Task.Delay(_batchPauseMs, cancellationToken);
                }
                else if (_sendDelayMs > 0)
                {
                    await Task.Delay(_sendDelayMs, cancellationToken);
                }
            }
        }

        return results;
    }

    // ── Get Next Reminder Type ───────────────────────────────────────

    public async Task<NextReminderResult?> GetNextReminderTypeAsync(
        int ticketKey, string dueDate, int daysLate, CancellationToken cancellationToken = default)
    {
        List<string> sentTypes = await _db.SmsReminders
            .AsNoTracking()
            .Where(r => r.TicketKey == ticketKey && r.DueDate == dueDate && r.Status == 1)
            .Select(r => r.ReminderType!)
            .ToListAsync(cancellationToken);

        HashSet<string> sentSet = new(sentTypes);

        // Final reminder (30 days tier) already attempted — no further SMS for this ticket + due date.
        // Counts failures too (Status 0): one try at the final tier, then stop.
        bool finalReminderAttempted = await _db.SmsReminders.AsNoTracking()
            .AnyAsync(r => r.TicketKey == ticketKey
                        && r.DueDate == dueDate
                        && (r.ReminderType == "reminder_30" || r.ReminderType == "combined_30"),
                cancellationToken);
        if (finalReminderAttempted)
            return null;

        List<(int Interval, string Type)> applicable = new();

        foreach (int interval in ReminderIntervals)
        {
            string reminderType = $"reminder_{interval}";
            if (sentSet.Contains(reminderType))
                continue;

            if (daysLate >= interval)
                applicable.Add((interval, reminderType));
        }

        if (applicable.Count == 0)
            return null;

        applicable.Sort((a, b) => a.Interval.CompareTo(b.Interval));
        (int selectedInterval, string selectedType) = applicable[0];

        return new NextReminderResult
        {
            ReminderType = selectedType,
            DaysDiff = selectedInterval,
            Description = ReminderDescriptions.GetValueOrDefault(selectedInterval, $"{selectedInterval} days")
        };
    }

    // ── History & Statistics ─────────────────────────────────────────

    public async Task<List<ReminderHistoryItem>> GetReminderHistoryAsync(
        int? ticketKey = null, int? customerKey = null, string? phone = null,
        int limit = 50, CancellationToken cancellationToken = default)
    {
        IQueryable<SmsReminderEntity> query = _db.SmsReminders.AsNoTracking();

        if (ticketKey.HasValue)
            query = query.Where(r => r.TicketKey == ticketKey.Value);

        if (customerKey.HasValue)
            query = query.Where(r => r.CustomerKey == customerKey.Value);

        if (!string.IsNullOrEmpty(phone))
        {
            string? normalized = PhoneUtils.ExtractLast10Digits(phone);
            if (normalized is not null)
                query = query.Where(r => r.Phone != null && r.Phone.Contains(normalized));
        }

        List<SmsReminderEntity> entities = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToHistoryItem).ToList();
    }

    public async Task<List<ReminderStatistic>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _db.SmsReminders
            .AsNoTracking()
            .GroupBy(r => r.ReminderType)
            .Select(g => new
            {
                ReminderType = g.Key ?? "unknown",
                Total = g.Count(),
                Successful = g.Count(x => x.Status == 1),
                Failed = g.Count(x => x.Status == 0)
            })
            .OrderBy(g => g.ReminderType)
            .ToListAsync(cancellationToken);

        return groups.Select(g => new ReminderStatistic
        {
            ReminderType = g.ReminderType,
            Total = g.Total,
            Successful = g.Successful,
            Failed = g.Failed,
            SuccessRate = g.Total > 0 ? Math.Round((double)g.Successful / g.Total * 100, 1) : 0
        }).ToList();
    }

    public async Task<List<ReminderHistoryItem>> GetRecentRemindersAsync(
        int limit = 20, CancellationToken cancellationToken = default)
    {
        List<SmsReminderEntity> entities = await _db.SmsReminders
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToHistoryItem).ToList();
    }

    public async Task<List<SentReminderItem>> GetSentRemindersAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        var items = await _db.SmsReminders
            .AsNoTracking()
            .Where(r => r.Status == 1)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new
            {
                Reminder = r,
                Customer = _db.Customers.AsNoTracking()
                    .Where(c => c.CustomerKey == r.CustomerKey)
                    .Select(c => new { c.FirstName, c.LastName })
                    .FirstOrDefault(),
                TransNo = _db.Tickets.AsNoTracking()
                    .Where(t => t.Key == r.TicketKey)
                    .Select(t => t.TransNo)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return items.Select(x => new SentReminderItem
        {
            ReminderId = x.Reminder.Id,
            TicketKey = x.Reminder.TicketKey,
            CustomerKey = x.Reminder.CustomerKey,
            DueDate = x.Reminder.DueDate,
            Phone = x.Reminder.Phone,
            ReminderType = x.Reminder.ReminderType,
            Status = x.Reminder.Status == 1 ? "sent" : "failed",
            Message = x.Reminder.Message,
            SentAt = x.Reminder.CreatedAt,
            TwilioSid = x.Reminder.TwilioSid,
            CustomerName = BuildCustomerName(x.Customer?.FirstName, x.Customer?.LastName),
            TransNo = x.TransNo
        }).ToList();
    }

    // ── Exclusion / Unsubscribe ──────────────────────────────────────

    public async Task<bool> IsPhoneExcludedAsync(string phone, CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phone);
        if (normalized is null)
            return true; // invalid phone = treated as excluded

        bool excluded = await _db.SmsExcluded
            .AsNoTracking()
            .AnyAsync(e => e.Phone.Contains(normalized), cancellationToken);

        if (excluded)
            return true;

        bool unsubscribed = await _db.SmsUnsubscribed
            .AsNoTracking()
            .AnyAsync(u => u.Phone.Contains(normalized), cancellationToken);

        return unsubscribed;
    }

    public async Task<bool> AddToExcludedAsync(
        string phone, string? reason, int? userId, CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phone);
        if (normalized is null)
            return false;

        bool alreadyExists = await _db.SmsExcluded
            .AnyAsync(e => e.Phone == normalized, cancellationToken);

        if (alreadyExists)
            return true;

        _db.SmsExcluded.Add(new SmsExcludedEntity
        {
            Phone = normalized,
            Reason = reason,
            ExcludedBy = userId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddToUnsubscribedAsync(
        string phone, string method = "MANUAL", string? notes = null,
        CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phone);
        if (normalized is null)
            return false;

        bool alreadyExists = await _db.SmsUnsubscribed
            .AnyAsync(u => u.Phone == normalized, cancellationToken);

        if (alreadyExists)
            return true;

        _db.SmsUnsubscribed.Add(new SmsUnsubscribedEntity
        {
            Phone = normalized,
            Method = method,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private async Task<bool> WasReminderSentAsync(
        int ticketKey, string dueDate, string reminderType, CancellationToken cancellationToken)
    {
        return await _db.SmsReminders
            .AsNoTracking()
            .AnyAsync(r => r.TicketKey == ticketKey
                        && r.DueDate == dueDate
                        && r.ReminderType == reminderType
                        && r.Status == 1,
                cancellationToken);
    }

    private async Task AddToConversationThreadAsync(
        int storeId, string fromE164, string toE164, string body,
        string? twilioSid, CancellationToken cancellationToken)
    {
        if (_threadRepo is null || _messageRepo is null)
        {
            _logger.LogDebug("Conversation repos not available; skipping thread lookup");
            return;
        }

        try
        {
            TwilioNumber? storeNumber = await _storePhoneResolver.GetStoreNumberByPhoneAsync(
                fromE164, cancellationToken);
            if (storeNumber is null || storeNumber.StoreId != storeId)
            {
                _logger.LogWarning(
                    "Reminder sender {Sender} is not an active number for store {StoreId}; skipping conversation append",
                    fromE164, storeId);
                return;
            }

            var thread = await _threadRepo.FindOpenAsync(
                storeId, storeNumber.NumberId, toE164, cancellationToken);

            if (thread is null)
            {
                _logger.LogInformation(
                    "No open conversation thread for {Phone}; reminder is reminders-tab only", toE164);
                return;
            }

            var message = await _messageRepo.CreateOutboundAsync(
                storeId, thread.ThreadId, fromE164,
                fromE164, toE164, body,
                null, "reminder", null,
                cancellationToken);

            await _threadRepo.UpdateLastMessageAtAsync(thread.ThreadId, message.CreatedAt, cancellationToken);

            if (!string.IsNullOrEmpty(twilioSid))
                await _messageRepo.UpdateSentAsync(message.MessageId, twilioSid, "Sent", cancellationToken);

            _logger.LogInformation(
                "Appended reminder to existing thread {ThreadId} for {Phone}",
                thread.ThreadId, toE164);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to add reminder to conversation thread for {Phone}", toE164);
        }
    }

    private async Task LogReminderAsync(
        int ticketKey, int customerKey, string dueDate, string phone,
        string reminderType, string message, bool success, string? twilioSid,
        string? error, int? userId, int storeId, string storePhone,
        CancellationToken cancellationToken)
    {
        _db.SmsReminders.Add(new SmsReminderEntity
        {
            TicketKey = ticketKey,
            CustomerKey = customerKey,
            DueDate = dueDate,
            Phone = phone,
            ReminderType = reminderType,
            Message = message,
            Status = success ? 1 : 0,
            TwilioSid = twilioSid,
            ErrorMessage = error,
            SentByUserId = userId,
            StoreId = storeId,
            StorePhone = storePhone,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static DateTime ParseDueDate(string? dueDateStr)
    {
        if (string.IsNullOrEmpty(dueDateStr))
            return DateTime.Now;

        // Try M/d/yyyy format (XPD standard)
        if (DateTime.TryParseExact(dueDateStr, "M/d/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(dueDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fallback))
            return fallback;

        return DateTime.Now;
    }

    private static DateTime? ParseDueDateNullable(string? dueDateStr)
    {
        if (string.IsNullOrEmpty(dueDateStr))
            return null;

        if (DateTime.TryParseExact(dueDateStr, "M/d/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(dueDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fallback))
            return fallback;

        return null;
    }

    private static string? GetMessageTemplate(int daysDiff, string ticketNo, DateTime dueDate, string? storePhone)
    {
        string dueDateStr = dueDate.ToString("MM/dd/yyyy");

        string phoneSuffix = storePhone is not null ? $"\nQuestions? Call us at {storePhone}." : "";
        string phoneCall = storePhone is not null ? $"Call the store immediately at {storePhone}." : "Call the store immediately.";
        string phoneNow = storePhone is not null ? $"Call the store now {storePhone}." : "Call the store now.";
        string phoneCallNow = storePhone is not null ? $"Call {storePhone} NOW." : "Call the store NOW.";
        string phoneContact = storePhone is not null
            ? $"Please contact {storePhone} immediately if you wish to reclaim your item."
            : "Please contact the store immediately if you wish to reclaim your item.";

        return daysDiff switch
        {
            -7 => $"Friendly reminder from King Gold and Pawn\nTicket #: {ticketNo}\nYour pawn will expire in 7 days on {dueDateStr}.\nYou can extend or pick it up anytime.{phoneSuffix}",
             0 => $"Urgent notice from King Gold and Pawn\nTicket #: {ticketNo}\nYour pawn expires TODAY ({dueDateStr}).\nPlease act now to avoid losing your item.\n{phoneCall}",
             7 => $"Warning from King Gold and Pawn\nTicket #: {ticketNo}\nYour pawn expired 7 days ago on {dueDateStr}.\nYou are at risk of forfeiture. Act immediately. {phoneNow}",
            14 => $"Final warning from King Gold and Pawn\nTicket #: {ticketNo}\nYour pawn expired 14 days ago ({dueDateStr}).\nYour item is in serious jeopardy of being forfeited.\n{phoneCallNow}",
            30 => $"Final notice from King Gold and Pawn\nTicket #: {ticketNo}\nYour pawn expired 30 days ago ({dueDateStr}).\nThis is the last SMS you will receive for this ticket. No further communication will follow. {phoneContact}",
             _ => null
        };
    }

    private static string BuildCombinedMessage(List<CombinedTicketInfo> tickets, string? storePhoneDisplay)
    {
        int mostUrgentDays = 0;
        List<string> ticketLines = new();

        foreach (CombinedTicketInfo t in tickets)
        {
            if (t.DaysLate > mostUrgentDays)
                mostUrgentDays = t.DaysLate;

            if (t.DaysLate > 0)
                ticketLines.Add($"- Ticket #{t.TransNo} - {t.DaysLate} days overdue (due {t.DueDateStr})");
            else if (t.DaysLate == 0)
                ticketLines.Add($"- Ticket #{t.TransNo} - DUE TODAY ({t.DueDateStr})");
            else
                ticketLines.Add($"- Ticket #{t.TransNo} - Due in {-t.DaysLate} days ({t.DueDateStr})");
        }

        string intro = mostUrgentDays switch
        {
            >= 30 => "This is your final notice from King Gold and Pawn.",
            >= 14 => "Urgent notice from King Gold and Pawn.",
            >= 7  => "Warning from King Gold and Pawn.",
            >= 0  => "Reminder from King Gold and Pawn.",
            _     => "Friendly reminder from King Gold and Pawn."
        };

        string callText = storePhoneDisplay is not null
            ? $"Please visit the store or call {storePhoneDisplay} to avoid losing your items."
            : "Please visit the store to avoid losing your items.";

        if (mostUrgentDays >= 30)
            return $"{intro}\n\nThis is the last SMS you will receive for these tickets.\n\n{callText}";

        string ticketsText = string.Join("\n", ticketLines);
        return $"{intro}\n\nYou have {tickets.Count} pawns requiring attention:\n\n{ticketsText}\n\n{callText}";
    }

    private static string FormatPhoneDisplay(string phoneE164)
    {
        string digits = new(phoneE164.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1')
            digits = digits[1..];
        if (digits.Length == 10)
            return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
        return phoneE164;
    }

    private static string BuildCustomerName(string? firstName, string? lastName)
    {
        string name = $"{firstName ?? ""} {lastName ?? ""}".Trim();
        return string.IsNullOrEmpty(name) ? "Unknown" : name;
    }

    private static ReminderHistoryItem MapToHistoryItem(SmsReminderEntity entity)
    {
        return new ReminderHistoryItem
        {
            ReminderId = entity.Id,
            TicketKey = entity.TicketKey,
            CustomerKey = entity.CustomerKey,
            DueDate = entity.DueDate,
            Phone = entity.Phone,
            ReminderType = entity.ReminderType,
            Message = entity.Message,
            Status = entity.Status == 1 ? "sent" : "failed",
            TwilioSid = entity.TwilioSid,
            Error = entity.ErrorMessage,
            SentAt = entity.CreatedAt,
            SentByUserId = entity.SentByUserId,
            StoreId = entity.StoreId
        };
    }

    // Internal grouping helper for batch processing.
    private sealed class CustomerTicketGroup
    {
        public string Phone { get; set; } = string.Empty;
        public int CustomerKey { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public List<CombinedTicketInfo> Tickets { get; set; } = new();
    }
}
