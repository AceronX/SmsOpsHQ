namespace SmsOpsHQ.Core.Services;

// Contract for resolving phone numbers to XPD customer identities.
public interface IIdentityResolver
{
    // Resolve a phone number to a list of XPD customer keys.
    // Normalizes the phone and queries XPD_CustomerPhones by PhoneNormalized.
    Task<List<int>> ResolveCustomerKeysAsync(string phoneE164,
        CancellationToken cancellationToken = default);

    // Resolve a phone number to a single canonical identity ID for a store.
    // Returns MIN(CustomerKey) from the resolved keys, or null if not found.
    Task<int?> ResolveIdentityIdAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default);
}
