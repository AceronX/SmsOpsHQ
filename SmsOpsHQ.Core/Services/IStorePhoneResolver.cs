using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Services;

// Contract for resolving stores by their Twilio phone numbers.
public interface IStorePhoneResolver
{
    // Find the store that owns a given Twilio phone number.
    Task<Store?> GetStoreByPhoneAsync(string phone,
        CancellationToken cancellationToken = default);

    // Find the exact active store-side number represented by a phone.
    Task<TwilioNumber?> GetStoreNumberByPhoneAsync(string phone,
        CancellationToken cancellationToken = default);

    // Get the default outbound phone number (E.164) for a store.
    Task<string?> GetStorePhoneAsync(int storeId,
        CancellationToken cancellationToken = default);
}
