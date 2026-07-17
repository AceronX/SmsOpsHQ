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

    public async Task<List<Store>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        List<StoreEntity> entities = await _db.Stores
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.StoreId)
            .ToListAsync(cancellationToken);
        return entities.Select(MapToDomain).ToList();
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

    public async Task<TwilioNumber?> GetNumberByPhoneAsync(
        string phoneE164,
        CancellationToken cancellationToken = default)
    {
        TwilioNumberEntity? number = await _db.TwilioNumbers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.PhoneE164 == phoneE164 && t.IsActive,
                cancellationToken);

        return number is null
            ? null
            : new TwilioNumber
            {
                NumberId = number.NumberId,
                StoreId = number.StoreId,
                PhoneE164 = number.PhoneE164,
                FriendlyName = number.FriendlyName,
                TwilioSid = number.TwilioSid,
                MessagingServiceSid = number.MessagingServiceSid,
                CapabilitiesJson = number.CapabilitiesJson,
                IsActive = number.IsActive,
                CreatedAt = number.CreatedAt
            };
    }

    // Returns the default Twilio number's E.164 phone for a store.
    // Uses the store's DefaultNumberId to find the TwilioNumber.
    // Returns null if DefaultNumberId is 0 (no Twilio number set).
    public async Task<string?> GetDefaultNumberAsync(int storeId, CancellationToken cancellationToken = default)
    {
        StoreEntity? store = await _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

        if (store is null || store.DefaultNumberId == 0)
            return null;

        TwilioNumberEntity? number = await _db.TwilioNumbers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.NumberId == store.DefaultNumberId, cancellationToken);

        return number?.PhoneE164;
    }

    public async Task<Store> CreateAsync(string storeName, CancellationToken cancellationToken = default)
    {
        var entity = new StoreEntity
        {
            StoreName = storeName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Stores.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return MapToDomain(entity);
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
            DefaultNumberId = entity.DefaultNumberId,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
