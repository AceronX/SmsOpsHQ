using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for OptOut entities.
public interface IOptOutRepository
{
    // Check if a phone is opted out for a specific store.
    Task<bool> ExistsAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);

    // Get all opt-outs for a store.
    Task<List<OptOut>> GetAllAsync(int storeId,
        CancellationToken cancellationToken = default);

    // Record a new opt-out.
    Task AddAsync(int storeId, string phoneE164, string? reason,
        CancellationToken cancellationToken = default);

    // Remove an opt-out (re-subscribe a phone).
    Task RemoveAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);
}
