using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IStoreRepository.
// Provides store lookup by ID and by Twilio phone number.
public sealed class StoreRepository : IStoreRepository
{
    private readonly AppDbContext _db;

    public StoreRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Store?> GetByIdAsync(int storeId, CancellationToken cancellationToken = default)
    {
        StoreEntity? entity = await _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    // Looks up TwilioNumbers to find which store owns the given phone number.
    public async Task<Store?> GetByPhoneAsync(string toE164, CancellationToken cancellationToken = default)
    {
        TwilioNumberEntity? number = await _db.TwilioNumbers
            .AsNoTracking()
            .Include(t => t.Store)
            .FirstOrDefaultAsync(t => t.PhoneE164 == toE164 && t.IsActive, cancellationToken);

        return number?.Store is null ? null : MapToDomain(number.Store);
    }

    // Returns the default Twilio number's E.164 phone for a store.
    // Uses the store's DefaultNumberId to find the TwilioNumber.
    public async Task<string?> GetDefaultNumberAsync(int storeId, CancellationToken cancellationToken = default)
    {
        StoreEntity? store = await _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

        if (store?.DefaultNumberId is null)
            return null;

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.NumberId == store.DefaultNumberId.Value, cancellationToken);

        return number?.PhoneE164;
    }

    private static Store MapToDomain(StoreEntity entity)
    {
        return new Store
        {
            StoreId = entity.StoreId,
            StoreName = entity.StoreName,
            Address = entity.Address,
            City = entity.City,
            State = entity.State,
            Zip = entity.Zip,
            Phone = entity.Phone,
            DefaultNumberId = entity.DefaultNumberId,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
