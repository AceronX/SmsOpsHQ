using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Store entities.
public interface IStoreRepository
{
    // Find store by primary key. Returns null if not found.
    Task<Store?> GetByIdAsync(int storeId, CancellationToken cancellationToken = default);
}
