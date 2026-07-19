using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

public sealed class LateTicketPullRepository : ILateTicketPullRepository
{
    private readonly AppDbContext _db;

    public LateTicketPullRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LateTicketPull?> GetAsync(
        int storeId,
        int ticketKey,
        CancellationToken cancellationToken = default)
    {
        LateTicketPullEntity? entity = await _db.LateTicketPulls
            .AsNoTracking()
            .SingleOrDefaultAsync(
                pull => pull.StoreId == storeId && pull.TicketKey == ticketKey,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<LateTicketPull>> GetByStoreAsync(
        int storeId,
        CancellationToken cancellationToken = default)
    {
        return await _db.LateTicketPulls
            .AsNoTracking()
            .Where(pull => pull.StoreId == storeId)
            .OrderByDescending(pull => pull.PulledAtUtc)
            .ThenByDescending(pull => pull.LateTicketPullId)
            .Select(pull => new LateTicketPull
            {
                LateTicketPullId = pull.LateTicketPullId,
                StoreId = pull.StoreId,
                TicketKey = pull.TicketKey,
                CustomerKey = pull.CustomerKey,
                Reason = pull.Reason,
                PulledByUserId = pull.PulledByUserId,
                PulledAtUtc = pull.PulledAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LateTicketPull> PullAsync(
        int storeId,
        int ticketKey,
        int customerKey,
        string? reason,
        int pulledByUserId,
        CancellationToken cancellationToken = default)
    {
        LateTicketPull? existing = await GetAsync(storeId, ticketKey, cancellationToken);
        if (existing is not null)
            return existing;

        LateTicketPullEntity entity = new()
        {
            StoreId = storeId,
            TicketKey = ticketKey,
            CustomerKey = customerKey,
            Reason = reason,
            PulledByUserId = pulledByUserId,
            PulledAtUtc = DateTime.UtcNow
        };

        _db.LateTicketPulls.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return Map(entity);
        }
        catch (DbUpdateException)
        {
            // A concurrent request may have inserted the same unique StoreId/TicketKey.
            _db.Entry(entity).State = EntityState.Detached;
            LateTicketPullEntity winner = await _db.LateTicketPulls
                .AsNoTracking()
                .SingleAsync(
                    pull => pull.StoreId == storeId && pull.TicketKey == ticketKey,
                    cancellationToken);
            return Map(winner);
        }
    }

    public async Task RestoreAsync(
        int storeId,
        int ticketKey,
        CancellationToken cancellationToken = default)
    {
        await _db.LateTicketPulls
            .Where(pull => pull.StoreId == storeId && pull.TicketKey == ticketKey)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static LateTicketPull Map(LateTicketPullEntity entity) => new()
    {
        LateTicketPullId = entity.LateTicketPullId,
        StoreId = entity.StoreId,
        TicketKey = entity.TicketKey,
        CustomerKey = entity.CustomerKey,
        Reason = entity.Reason,
        PulledByUserId = entity.PulledByUserId,
        PulledAtUtc = entity.PulledAtUtc
    };
}
