using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Store entities.
public interface IStoreRepository
{
    // Find store by primary key. Returns null if not found.
    Task<Store?> GetByIdAsync(int storeId, CancellationToken cancellationToken = default);

    // Find the store that owns a Twilio number by phone (E.164).
    // Looks up TwilioNumbers and returns the associated store.
    Task<Store?> GetByPhoneAsync(string toE164, CancellationToken cancellationToken = default);

    // Get the default Twilio phone number (E.164) for a store.
    // Returns null if no default number is configured.
    Task<string?> GetDefaultNumberAsync(int storeId, CancellationToken cancellationToken = default);
}
