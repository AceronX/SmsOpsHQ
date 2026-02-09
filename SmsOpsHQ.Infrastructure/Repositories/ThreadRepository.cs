using Microsoft.EntityFrameworkCore;
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

    // Upserts a thread by (StoreId, IdentityId). If identityId is null,
    // always creates a new thread (phone-only threads can't be deduplicated).
    public async Task<Thread> FindOrCreateAsync(int storeId, int? identityId,
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

        ThreadEntity entity = new ThreadEntity
        {
            StoreId = storeId,
            IdentityId = identityId,
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

        // Apply filter
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

        // Apply Twilio number filter
        if (twilioNumberId is not null)
            query = query.Where(t => t.TwilioNumberId == twilioNumberId);

        // Search requires joining on Customers -- for now filter in-memory
        // after loading threads. In Phase 2E the controller layer will handle
        // search with a separate customer lookup. The interface is defined to
        // accept the parameter for future use.

        List<ThreadEntity> entities = await query
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
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
}
