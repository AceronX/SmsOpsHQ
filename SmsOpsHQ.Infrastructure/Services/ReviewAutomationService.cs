using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// Detects new ticket Keys synced from XPD and sends one review request per affected CustomerKey.
public sealed class ReviewAutomationService : IReviewAutomationService
{
    private readonly AppDbContext _db;
    private readonly IReviewService _reviewService;
    private readonly IReminderService _reminderService;
    private readonly ILogger<ReviewAutomationService> _logger;

    public ReviewAutomationService(
        AppDbContext db,
        IReviewService reviewService,
        IReminderService reminderService,
        ILogger<ReviewAutomationService> logger)
    {
        _db = db;
        _reviewService = reviewService;
        _reminderService = reminderService;
        _logger = logger;
    }

    public async Task<ReviewAutomationResult> ProcessNewTicketsAsync(CancellationToken cancellationToken = default)
    {
        ReviewAutomationResult result = new();

        const int stateId = 1;
        ReviewAutomationStateEntity? state = await _db.ReviewAutomationState
            .SingleOrDefaultAsync(s => s.StateId == stateId, cancellationToken);

        if (state is null)
        {
            state = new ReviewAutomationStateEntity { StateId = stateId, LastMaxTicketKey = null };
            _db.ReviewAutomationState.Add(state);
            await _db.SaveChangesAsync(cancellationToken);
        }

        int dbMaxKey = await _db.Tickets.AsNoTracking()
            .Select(t => (int?)t.Key)
            .MaxAsync(cancellationToken) ?? 0;

        // First deployment: align watermark with current data without messaging everyone.
        if (state.LastMaxTicketKey is null)
        {
            state.LastMaxTicketKey = dbMaxKey;
            await _db.SaveChangesAsync(cancellationToken);
            result.Detail = "bootstrap";
            result.Skipped = 0;
            _logger.LogInformation(
                "Review automation bootstrapped: LastMaxTicketKey set to {Key} (no messages sent).",
                dbMaxKey);
            return result;
        }

        int watermark = state.LastMaxTicketKey.Value;

        List<int> newCustomerKeys = await _db.Tickets.AsNoTracking()
            .Where(t => t.Key > watermark)
            .Select(t => t.CustomerKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (newCustomerKeys.Count == 0)
        {
            result.Detail = "no_new_tickets";
            return result;
        }

        foreach (int customerKey in newCustomerKeys)
        {
            CustomerEntity? customer = await _db.Customers.AsNoTracking()
                .Where(c => c.CustomerKey == customerKey)
                .OrderBy(c => c.StoreId)
                .FirstOrDefaultAsync(cancellationToken);

            if (customer is null)
            {
                result.Skipped++;
                _logger.LogWarning(
                    "Review automation: no customer row for CustomerKey={Key}",
                    customerKey);
                continue;
            }

            string? phone = PickBestPhone(customer.ResPhone, customer.BusPhone, customer.Notes);
            if (phone is null)
            {
                result.Skipped++;
                _logger.LogInformation(
                    "Review automation: no dialable phone for CustomerKey={Key}",
                    customerKey);
                continue;
            }

            if (await _reminderService.IsPhoneExcludedAsync(phone, cancellationToken))
            {
                result.Skipped++;
                _logger.LogInformation(
                    "Review automation: excluded/unsubscribed phone for CustomerKey={Key}",
                    customerKey);
                continue;
            }

            try
            {
                await _reviewService.SendReviewRequestAsync(customer.StoreId, phone, cancellationToken);
                result.Sent++;
                _logger.LogInformation(
                    "Review automation: sent for CustomerKey={Key} StoreId={StoreId}",
                    customerKey, customer.StoreId);
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.LogWarning(ex,
                    "Review automation: send failed for CustomerKey={Key} StoreId={StoreId}",
                    customerKey, customer.StoreId);
            }
        }

        state.LastMaxTicketKey = dbMaxKey;
        await _db.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Prefer residential, then business, then first number extracted from notes (same spirit as manual flows).
    /// </summary>
    internal static string? PickBestPhone(string? resPhone, string? busPhone, string? notes)
    {
        foreach (string? raw in new[] { resPhone, busPhone })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string? e164 = PhoneUtils.NormalizeToE164(raw);
            if (e164 is not null)
                return e164;
        }

        foreach (string extracted in PhoneUtils.ExtractPhonesFromText(notes))
        {
            string? e164 = PhoneUtils.NormalizeToE164(extracted);
            if (e164 is not null)
                return e164;
        }

        return null;
    }
}
