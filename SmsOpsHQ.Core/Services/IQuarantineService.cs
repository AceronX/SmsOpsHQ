using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Services;

// Contract for quarantining and resolving suspicious inbound messages.
public interface IQuarantineService
{
    // Quarantine an inbound message. Returns the quarantine record ID.
    Task<int> QuarantineMessageAsync(
        int storeId, string fromE164, string toE164,
        string? body, string? mediaJson, string? twilioSid, string reason,
        CancellationToken cancellationToken = default);

    // Get quarantined messages with optional resolution filter.
    Task<List<QuarantinedMessage>> GetMessagesAsync(int limit = 50,
        string? resolution = null,
        CancellationToken cancellationToken = default);

    // Resolve a quarantined message (approve, reject, or mark as spam).
    Task<bool> ResolveAsync(int quarantineId, string resolution, int? userId,
        CancellationToken cancellationToken = default);
}
