using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// Quarantines suspicious inbound messages and provides review/resolution.
public sealed class QuarantineService : IQuarantineService
{
    private readonly AppDbContext _db;

    public QuarantineService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> QuarantineMessageAsync(
        int storeId, string fromE164, string toE164,
        string? body, string? mediaJson, string? twilioSid, string reason,
        CancellationToken cancellationToken = default)
    {
        QuarantinedMessageEntity entity = new QuarantinedMessageEntity
        {
            StoreId = storeId,
            FromE164 = fromE164,
            ToE164 = toE164,
            Body = body,
            MediaJson = mediaJson,
            TwilioSid = twilioSid,
            QuarantineReason = reason,
            QuarantinedAt = DateTime.UtcNow
        };

        _db.QuarantinedMessages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return entity.QuarantineId;
    }

    public async Task<List<QuarantinedMessage>> GetMessagesAsync(int limit = 50,
        string? resolution = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<QuarantinedMessageEntity> query = _db.QuarantinedMessages
            .AsNoTracking();

        if (resolution is not null)
        {
            query = query.Where(q => q.Resolution == resolution);
        }
        else
        {
            // Default: show unresolved (pending) messages
            query = query.Where(q => q.Resolution == null);
        }

        List<QuarantinedMessageEntity> entities = await query
            .OrderByDescending(q => q.QuarantinedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<bool> ResolveAsync(int quarantineId, string resolution, int? userId,
        CancellationToken cancellationToken = default)
    {
        QuarantinedMessageEntity? entity = await _db.QuarantinedMessages
            .FirstOrDefaultAsync(q => q.QuarantineId == quarantineId, cancellationToken);

        if (entity is null)
            return false;

        entity.Resolution = resolution;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static QuarantinedMessage MapToDomain(QuarantinedMessageEntity entity)
    {
        return new QuarantinedMessage
        {
            QuarantineId = entity.QuarantineId,
            StoreId = entity.StoreId,
            FromE164 = entity.FromE164,
            ToE164 = entity.ToE164,
            Body = entity.Body,
            MediaJson = entity.MediaJson,
            TwilioSid = entity.TwilioSid,
            QuarantineReason = entity.QuarantineReason,
            QuarantinedAt = entity.QuarantinedAt,
            ReviewedAt = entity.ReviewedAt,
            ReviewedByUserId = entity.ReviewedByUserId,
            Resolution = entity.Resolution
        };
    }
}
