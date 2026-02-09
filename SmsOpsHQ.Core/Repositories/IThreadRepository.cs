using SmsOpsHQ.Core.Entities;
using Thread = SmsOpsHQ.Core.Entities.Thread;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Thread entities.
public interface IThreadRepository
{
    // Find an existing thread or create a new one by (StoreId, IdentityId).
    Task<Thread> FindOrCreateAsync(int storeId, int? identityId,
        CancellationToken cancellationToken = default);

    // Get inbox threads for a store, ordered by LastMessageAt DESC.
    Task<List<Thread>> GetInboxAsync(int storeId, string? filter, string? search,
        int? twilioNumberId, CancellationToken cancellationToken = default);

    // Get a single thread with store isolation.
    Task<Thread?> GetByIdAsync(int storeId, int threadId,
        CancellationToken cancellationToken = default);

    // Bump the thread's last-message timestamp.
    Task UpdateLastMessageAtAsync(int threadId, DateTime utcNow,
        CancellationToken cancellationToken = default);

    // Atomically increment unread count by 1.
    Task IncrementUnreadAsync(int threadId,
        CancellationToken cancellationToken = default);

    // Reset unread count to 0.
    Task MarkReadAsync(int threadId,
        CancellationToken cancellationToken = default);

    // Delete a single thread with store isolation.
    Task DeleteAsync(int storeId, int threadId,
        CancellationToken cancellationToken = default);

    // Delete all threads for a store.
    Task DeleteAllAsync(int storeId,
        CancellationToken cancellationToken = default);

    // Bulk-load threads by a list of IDs, scoped to a store.
    Task<List<Thread>> GetByIdsAsync(int storeId, List<int> threadIds,
        CancellationToken cancellationToken = default);
}
