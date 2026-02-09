using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IMessageRepository.
// Handles message CRUD, Twilio SID lookups, status updates, and notes.
public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _db;

    public MessageRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Message?> FindBySidAsync(string twilioSid, CancellationToken cancellationToken = default)
    {
        MessageEntity? entity = await _db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TwilioSid == twilioSid, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<Message> CreateOutboundAsync(
        int storeId, int threadId, string storePhone,
        string fromE164, string toE164, string body,
        string? mediaJson, string category, int? sentByUserId,
        CancellationToken cancellationToken = default)
    {
        MessageEntity entity = new MessageEntity
        {
            StoreId = storeId,
            ThreadId = threadId,
            StorePhone = storePhone,
            Direction = "Outbound",
            FromE164 = fromE164,
            ToE164 = toE164,
            Body = body,
            MediaJson = mediaJson,
            Category = category,
            Status = "Queued",
            SentByUserId = sentByUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    public async Task<Message> CreateInboundAsync(
        int storeId, int threadId, string storePhone,
        string fromE164, string toE164, string body,
        string? mediaJson, string category,
        CancellationToken cancellationToken = default)
    {
        MessageEntity entity = new MessageEntity
        {
            StoreId = storeId,
            ThreadId = threadId,
            StorePhone = storePhone,
            Direction = "Inbound",
            FromE164 = fromE164,
            ToE164 = toE164,
            Body = body,
            MediaJson = mediaJson,
            Category = category,
            Status = "Received",
            CreatedAt = DateTime.UtcNow
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    public async Task UpdateSentAsync(int messageId, string twilioSid, string status,
        CancellationToken cancellationToken = default)
    {
        MessageEntity? entity = await _db.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

        if (entity is null)
            return;

        entity.TwilioSid = twilioSid;
        entity.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusBySidAsync(string twilioSid, string status,
        string? errorCode, string? errorText,
        CancellationToken cancellationToken = default)
    {
        MessageEntity? entity = await _db.Messages
            .FirstOrDefaultAsync(m => m.TwilioSid == twilioSid, cancellationToken);

        if (entity is null)
            return;

        entity.Status = status;
        entity.ErrorCode = errorCode;
        entity.ErrorText = errorText;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Message>> GetByThreadAsync(int storeId, int threadId, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        List<MessageEntity> entities = await _db.Messages
            .AsNoTracking()
            .Where(m => m.StoreId == storeId && m.ThreadId == threadId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<Message?> GetLastMessageAsync(int threadId,
        CancellationToken cancellationToken = default)
    {
        MessageEntity? entity = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ThreadId == threadId)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<Message> CreateNoteAsync(int storeId, int threadId, string content, int userId,
        CancellationToken cancellationToken = default)
    {
        MessageEntity entity = new MessageEntity
        {
            StoreId = storeId,
            ThreadId = threadId,
            Direction = "Note",
            FromE164 = "system",
            ToE164 = "system",
            Body = content,
            Category = "general",
            Status = "Internal",
            SentByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    public async Task<(List<Message> Messages, int TotalCount)> GetPagedAsync(
        int storeId, string? category, int? threadId, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MessageEntity> query = _db.Messages
            .AsNoTracking()
            .Include(m => m.Thread)
            .Where(m => m.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
            query = query.Where(m => m.Category == category);

        if (threadId is not null)
            query = query.Where(m => m.ThreadId == threadId.Value);

        int totalCount = await query.CountAsync(cancellationToken);

        List<MessageEntity> entities = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (entities.Select(MapToDomain).ToList(), totalCount);
    }

    public async Task<Dictionary<string, int>> GetCountsByCategoryAsync(
        int storeId, int? threadId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MessageEntity> query = _db.Messages
            .AsNoTracking()
            .Include(m => m.Thread)
            .Where(m => m.Thread.StoreId == storeId);

        if (threadId is not null)
            query = query.Where(m => m.ThreadId == threadId.Value);

        List<CategoryCount> grouped = await query
            .GroupBy(m => m.Category)
            .Select(g => new CategoryCount { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        Dictionary<string, int> counts = new()
        {
            { "reminder", 0 },
            { "directions", 0 },
            { "promotions", 0 },
            { "general", 0 }
        };

        foreach (CategoryCount item in grouped)
        {
            if (counts.ContainsKey(item.Category))
                counts[item.Category] = item.Count;
        }

        return counts;
    }

    public async Task<List<Message>> GetAllByStoreAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<MessageEntity> entities = await _db.Messages
            .AsNoTracking()
            .Include(m => m.Thread)
            .Where(m => m.Thread.StoreId == storeId)
            .OrderBy(m => m.ThreadId)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    // Internal projection type for EF Core GroupBy.
    private sealed class CategoryCount
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private static Message MapToDomain(MessageEntity entity)
    {
        return new Message
        {
            MessageId = entity.MessageId,
            ThreadId = entity.ThreadId,
            StoreId = entity.StoreId,
            StorePhone = entity.StorePhone,
            Direction = entity.Direction,
            FromE164 = entity.FromE164,
            ToE164 = entity.ToE164,
            Body = entity.Body,
            MediaJson = entity.MediaJson,
            Category = entity.Category,
            Status = entity.Status,
            TwilioSid = entity.TwilioSid,
            SentByUserId = entity.SentByUserId,
            ErrorCode = entity.ErrorCode,
            ErrorText = entity.ErrorText,
            CreatedAt = entity.CreatedAt
        };
    }
}
