using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Thread = SmsOpsHQ.Core.Entities.Thread;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IThreadRepository.
// Handles thread find/create, inbox queries, unread tracking, and deletion.
public sealed class ThreadRepository : IThreadRepository
{
    private readonly AppDbContext _db;

    public ThreadRepository(AppDbContext db)
    {
        _db = db;
    }

    // Upserts a thread by (StoreId, IdentityId), then falls back to (StoreId, CustomerId).
    // This ensures unknown contacts (identityId=null) still reuse existing threads.
    public async Task<Thread> FindOrCreateAsync(int storeId, int? identityId, int? customerId = null,
        CancellationToken cancellationToken = default)
    {
        if (identityId is not null)
        {
            ThreadEntity? existing = await _db.Threads
                .FirstOrDefaultAsync(
                    t => t.StoreId == storeId && t.IdentityId == identityId,
                    cancellationToken);

            if (existing is not null)
                return MapToDomain(existing);
        }

        if (customerId is not null && customerId != 0)
        {
            ThreadEntity? existing = await _db.Threads
                .FirstOrDefaultAsync(
                    t => t.StoreId == storeId && t.CustomerId == customerId,
                    cancellationToken);

            if (existing is not null)
                return MapToDomain(existing);
        }

        ThreadEntity entity = new ThreadEntity
        {
            StoreId = storeId,
            IdentityId = identityId,
            CustomerId = customerId,
            Status = "Open",
            UnreadCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.Threads.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    // Get inbox threads for a store, ordered by LastMessageAt DESC.
    // Supports filter (all/unread/open/closed), search by customer name/phone,
    // and optional Twilio number filtering.
    public async Task<List<Thread>> GetInboxAsync(int storeId, string? filter, string? search,
        int? twilioNumberId, CancellationToken cancellationToken = default)
    {
        IQueryable<ThreadEntity> query = _db.Threads
            .AsNoTracking()
            .Where(t => t.StoreId == storeId);

        query = ApplyInboxFilters(query, filter, search, twilioNumberId);

        List<ThreadEntity> entities = await query
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    // Get inbox threads with customer data in one query (avoids N+1). Same filters and search as GetInboxAsync.
    public async Task<List<(Thread thread, Customer? customer)>> GetInboxWithCustomersAsync(int storeId, string? filter, string? search,
        int? twilioNumberId, CancellationToken cancellationToken = default)
    {
        IQueryable<ThreadEntity> query = _db.Threads
            .AsNoTracking()
            .Include(t => t.Customer)
            .Where(t => t.StoreId == storeId);

        query = ApplyInboxFilters(query, filter, search, twilioNumberId);

        List<ThreadEntity> entities = await query
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync(cancellationToken);

        return entities.Select(t => (MapToDomain(t), t.Customer is not null ? MapCustomerToDomain(t.Customer) : null)).ToList();
    }

    private static IQueryable<ThreadEntity> ApplyInboxFilters(IQueryable<ThreadEntity> query, string? filter, string? search, int? twilioNumberId)
    {
        // Apply status filter
        if (!string.IsNullOrWhiteSpace(filter))
        {
            string f = filter.ToLowerInvariant();
            if (f == "unread")
                query = query.Where(t => t.UnreadCount > 0);
            else if (f == "open")
                query = query.Where(t => t.Status == "Open");
            else if (f == "closed")
                query = query.Where(t => t.Status == "Closed");
        }

        // Apply Twilio number filter: show threads for this number OR threads with no number set (legacy/unassigned).
        // Otherwise threads with TwilioNumberId=null would never appear when the store has a default number.
        if (twilioNumberId is not null)
            query = query.Where(t => t.TwilioNumberId == twilioNumberId || t.TwilioNumberId == null);

        // Search by customer name or phone (join to Customer via navigation)
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(t => t.CustomerId != null && t.Customer != null &&
                ((t.Customer.FirstName != null && t.Customer.FirstName.Contains(term)) ||
                 (t.Customer.LastName != null && t.Customer.LastName.Contains(term)) ||
                 (t.Customer.PhoneE164 != null && t.Customer.PhoneE164.Contains(term))));
        }

        return query;
    }

    public async Task<Thread?> GetByIdAsync(int storeId, int threadId,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.StoreId == storeId && t.ThreadId == threadId,
                cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task UpdateCustomerIdAsync(int threadId, int customerId,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .FirstOrDefaultAsync(t => t.ThreadId == threadId, cancellationToken);

        if (entity is null)
            return;

        entity.CustomerId = customerId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLastMessageAtAsync(int threadId, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .FirstOrDefaultAsync(t => t.ThreadId == threadId, cancellationToken);

        if (entity is null)
            return;

        entity.LastMessageAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task IncrementUnreadAsync(int threadId,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .FirstOrDefaultAsync(t => t.ThreadId == threadId, cancellationToken);

        if (entity is null)
            return;

        entity.UnreadCount++;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkReadAsync(int threadId,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .FirstOrDefaultAsync(t => t.ThreadId == threadId, cancellationToken);

        if (entity is null)
            return;

        entity.UnreadCount = 0;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int storeId, int threadId,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .FirstOrDefaultAsync(
                t => t.StoreId == storeId && t.ThreadId == threadId,
                cancellationToken);

        if (entity is null)
            return;

        _db.Threads.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAllAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<ThreadEntity> entities = await _db.Threads
            .Where(t => t.StoreId == storeId)
            .ToListAsync(cancellationToken);

        _db.Threads.RemoveRange(entities);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Thread>> GetByIdsAsync(int storeId, List<int> threadIds,
        CancellationToken cancellationToken = default)
    {
        List<ThreadEntity> entities = await _db.Threads
            .AsNoTracking()
            .Where(t => t.StoreId == storeId && threadIds.Contains(t.ThreadId))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    private static Thread MapToDomain(ThreadEntity entity)
    {
        return new Thread
        {
            ThreadId = entity.ThreadId,
            StoreId = entity.StoreId,
            CustomerId = entity.CustomerId,
            TwilioNumberId = entity.TwilioNumberId,
            IdentityId = entity.IdentityId,
            Status = entity.Status,
            AssignedToUserId = entity.AssignedToUserId,
            LastMessageAt = entity.LastMessageAt,
            UnreadCount = entity.UnreadCount,
            CreatedAt = entity.CreatedAt
        };
    }

    private static Customer MapCustomerToDomain(CustomerEntity entity)
    {
        return new Customer
        {
            CustomerId = entity.CustomerId,
            StoreId = entity.StoreId,
            PhoneE164 = entity.PhoneE164,
            FirstName = entity.FirstName,
            LastName = entity.LastName
        };
    }
}
