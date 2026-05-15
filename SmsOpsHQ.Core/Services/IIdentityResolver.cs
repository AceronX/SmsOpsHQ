using SmsOpsHQ.Core.Models;

namespace SmsOpsHQ.Core.Services;

// Contract for resolving phone numbers to pawn customer identities.
public interface IIdentityResolver
{
    // Resolve a phone number to a list of customer keys. Uses CustomerPhones index (fast lookup).
    Task<List<int>> ResolveCustomerKeysAsync(string phoneE164,
        CancellationToken cancellationToken = default);

    // Full CustomerPhones rows for a normalized phone (source + direct flag).
    Task<List<CustomerPhoneMatch>> ResolveCustomerPhoneMatchesAsync(string phoneE164,
        CancellationToken cancellationToken = default);

    // Resolve a phone number to a single canonical identity ID for a store.
    // Returns MIN(CustomerKey) from the resolved keys, or null if not found.
    Task<int?> ResolveIdentityIdAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);
}
