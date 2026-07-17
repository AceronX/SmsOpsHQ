using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Resolves stores by their Twilio phone numbers.
// Delegates to IStoreRepository for TwilioNumbers lookup.
public sealed class StorePhoneResolver : IStorePhoneResolver
{
    private readonly IStoreRepository _storeRepository;

    public StorePhoneResolver(IStoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    // Find the store that owns the given phone number via TwilioNumbers lookup.
    public async Task<Store?> GetStoreByPhoneAsync(string phone,
        CancellationToken cancellationToken = default)
    {
        return await _storeRepository.GetByPhoneAsync(phone, cancellationToken);
    }

    public async Task<TwilioNumber?> GetStoreNumberByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        return await _storeRepository.GetNumberByPhoneAsync(phone, cancellationToken);
    }

    // Get the default outbound Twilio phone (E.164) for a store.
    public async Task<string?> GetStorePhoneAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        return await _storeRepository.GetDefaultNumberAsync(storeId, cancellationToken);
    }
}
