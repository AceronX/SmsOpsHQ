using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Store entities.
public interface IStoreRepository
{
    // Get all active stores (for HQ). For store-scoped users, filter to their store in the API layer.
    Task<List<Store>> GetAllAsync(CancellationToken cancellationToken = default);

    // Find store by primary key. Returns null if not found.
    Task<Store?> GetByIdAsync(int storeId, CancellationToken cancellationToken = default);

    // Find the store that owns a Twilio number by phone (E.164).
    // Looks up TwilioNumbers and returns the associated store.
    Task<Store?> GetByPhoneAsync(string toE164, CancellationToken cancellationToken = default);

    // Find the active store number represented by an E.164 phone.
    Task<TwilioNumber?> GetNumberByPhoneAsync(string phoneE164,
        CancellationToken cancellationToken = default);

    // Get the default Twilio phone number (E.164) for a store.
    // Returns null if no default number is configured.
    Task<string?> GetDefaultNumberAsync(int storeId, CancellationToken cancellationToken = default);

    // Create a new store (HQ only). Returns the created store.
    Task<Store> CreateAsync(string storeName, CancellationToken cancellationToken = default);
}
