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

    public async Task<Thread> FindOrCreateAsync(
        int storeId,
        int twilioNumberId,
        string contactPhoneE164,
        int? identityId,
        int? customerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactPhoneE164))
            throw new ArgumentException("A normalized contact phone is required.", nameof(contactPhoneE164));

        ThreadEntity? existing = await _db.Threads
            .FirstOrDefaultAsync(
                t => t.StoreId == storeId
                     && t.TwilioNumberId == twilioNumberId
                     && t.ContactPhoneE164 == contactPhoneE164,
                cancellationToken);

        if (existing is not null)
            return MapToDomain(existing);

        ThreadEntity entity = new ThreadEntity
        {
            StoreId = storeId,
            TwilioNumberId = twilioNumberId,
            ContactPhoneE164 = contactPhoneE164,
            IdentityId = identityId,
            CustomerId = customerId,
            Status = "Open",
            UnreadCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.Threads.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(entity).State = EntityState.Detached;
            ThreadEntity? concurrent = await _db.Threads
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.StoreId == storeId
                         && t.TwilioNumberId == twilioNumberId
                         && t.ContactPhoneE164 == contactPhoneE164,
                    cancellationToken);
            if (concurrent is not null)
                return MapToDomain(concurrent);
            throw;
        }

        return MapToDomain(entity);
    }

    public async Task<Thread?> FindOpenAsync(
        int storeId,
        int twilioNumberId,
        string contactPhoneE164,
        CancellationToken cancellationToken = default)
    {
        ThreadEntity? entity = await _db.Threads
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.StoreId == storeId
                                      && t.TwilioNumberId == twilioNumberId
                                      && t.ContactPhoneE164 == contactPhoneE164
                                      && t.Status == "Open",
                cancellationToken);

        return entity is null ? null : MapToDomain(entity);
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

        // Search by the conversation phone or customer profile metadata.
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(t =>
                (t.ContactPhoneE164 != null && t.ContactPhoneE164.Contains(term)) ||
                (t.CustomerId != null && t.Customer != null &&
                 ((t.Customer.FirstName != null && t.Customer.FirstName.Contains(term)) ||
                 (t.Customer.LastName != null && t.Customer.LastName.Contains(term)) ||
                 (t.Customer.PhoneE164 != null && t.Customer.PhoneE164.Contains(term)))));
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
            ContactPhoneE164 = entity.ContactPhoneE164,
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
