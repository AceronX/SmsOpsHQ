using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Customer entities.
public interface ICustomerRepository
{
    // Find an existing customer by phone or create a new one.
    Task<Customer> FindOrCreateAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);

    // Find an existing customer by phone without creating one. Returns null if not found.
    Task<Customer?> FindByPhoneAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);

    // Get a single customer with store isolation.
    Task<Customer?> GetByIdAsync(int storeId, int customerId,
        CancellationToken cancellationToken = default);

    // Search customers by name or phone within a store.
    Task<List<Customer>> SearchAsync(int storeId, string query, int limit = 20,
        CancellationToken cancellationToken = default);

    // Partial update of customer fields. Only non-null values are applied.
    Task UpdateAsync(int customerId, string? notes, string? firstName,
        string? lastName, string? tagsJson,
        CancellationToken cancellationToken = default);
}
