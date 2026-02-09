using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IOptOutRepository.
// Handles SMS opt-out compliance (add, check, remove).
public sealed class OptOutRepository : IOptOutRepository
{
    private readonly AppDbContext _db;

    public OptOutRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExistsAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default)
    {
        return await _db.OptOuts
            .AsNoTracking()
            .AnyAsync(o => o.StoreId == storeId && o.PhoneE164 == phoneE164, cancellationToken);
    }

    public async Task<List<OptOut>> GetAllAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<OptOutEntity> entities = await _db.OptOuts
            .AsNoTracking()
            .Where(o => o.StoreId == storeId)
            .OrderByDescending(o => o.OptOutDate)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task AddAsync(int storeId, string phoneE164, string? reason,
        CancellationToken cancellationToken = default)
    {
        // Idempotent: don't insert if already exists
        bool exists = await ExistsAsync(storeId, phoneE164, cancellationToken);
        if (exists)
            return;

        OptOutEntity entity = new OptOutEntity
        {
            StoreId = storeId,
            PhoneE164 = phoneE164,
            Reason = reason,
            OptOutDate = DateTime.UtcNow
        };

        _db.OptOuts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default)
    {
        OptOutEntity? entity = await _db.OptOuts
            .FirstOrDefaultAsync(
                o => o.StoreId == storeId && o.PhoneE164 == phoneE164,
                cancellationToken);

        if (entity is null)
            return;

        _db.OptOuts.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static OptOut MapToDomain(OptOutEntity entity)
    {
        return new OptOut
        {
            OptOutId = entity.OptOutId,
            StoreId = entity.StoreId,
            PhoneE164 = entity.PhoneE164,
            OptOutDate = entity.OptOutDate,
            Reason = entity.Reason
        };
    }
}
