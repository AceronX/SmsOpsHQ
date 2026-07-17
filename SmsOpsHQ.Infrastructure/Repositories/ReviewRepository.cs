using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IReviewRepository.
public sealed class ReviewRepository : IReviewRepository
{
    private readonly AppDbContext _db;

    public ReviewRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ReviewChannel>> GetActiveChannelsAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<ReviewChannelEntity> entities = await _db.ReviewChannels
            .AsNoTracking()
            .Where(r => r.StoreId == storeId && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken);

        return entities.Select(MapChannelToDomain).ToList();
    }

    public async Task<List<ReviewChannel>> GetChannelsAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<ReviewChannelEntity> entities = await _db.ReviewChannels
            .AsNoTracking()
            .Where(r => r.StoreId == storeId)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken);

        return entities.Select(MapChannelToDomain).ToList();
    }

    public async Task<ReviewRequest?> GetLastRequestForCustomerAsync(int storeId, int customerId,
        CancellationToken cancellationToken = default)
    {
        ReviewRequestEntity? entity = await _db.ReviewRequests
            .AsNoTracking()
            .Where(r => r.StoreId == storeId && r.CustomerId == customerId)
            .OrderByDescending(r => r.SentAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapRequestToDomain(entity);
    }

    public async Task<int> GetTemplateUsageCountAsync(int storeId, int customerId, int templateId,
        CancellationToken cancellationToken = default)
    {
        return await _db.ReviewRequests
            .AsNoTracking()
            .CountAsync(r => r.StoreId == storeId && r.CustomerId == customerId && r.TemplateId == templateId,
                cancellationToken);
    }

    public async Task<ReviewRequest> CreateRequestAsync(ReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewRequestEntity entity = new()
        {
            StoreId = request.StoreId,
            CustomerId = request.CustomerId,
            PhoneE164 = request.PhoneE164,
            ReviewChannelId = request.ReviewChannelId,
            TemplateId = request.TemplateId,
            MessageBody = request.MessageBody,
            TwilioSid = request.TwilioSid,
            Status = request.Status,
            ProviderStatus = request.ProviderStatus,
            ErrorCode = request.ErrorCode,
            ErrorMessage = request.ErrorMessage,
            DeliveredAt = request.DeliveredAt,
            SentAt = request.SentAt
        };

        _db.ReviewRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        request.ReviewRequestId = entity.ReviewRequestId;
        return request;
    }

    public async Task<ReviewRequest?> FindByTwilioSidAsync(string twilioSid,
        CancellationToken cancellationToken = default)
    {
        ReviewRequestEntity? entity = await _db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TwilioSid == twilioSid, cancellationToken);

        return entity is null ? null : MapRequestToDomain(entity);
    }

    public async Task<bool> UpdateStatusByTwilioSidAsync(
        string twilioSid,
        string status,
        string providerStatus,
        string? errorCode,
        string? errorMessage,
        DateTime? deliveredAt,
        CancellationToken cancellationToken = default)
    {
        ReviewRequestEntity? entity = await _db.ReviewRequests
            .FirstOrDefaultAsync(r => r.TwilioSid == twilioSid, cancellationToken);
        if (entity is null)
            return false;

        entity.Status = status;
        entity.ProviderStatus = providerStatus;
        entity.ErrorCode = errorCode;
        entity.ErrorMessage = errorMessage;
        if (deliveredAt.HasValue && !entity.DeliveredAt.HasValue)
            entity.DeliveredAt = deliveredAt;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<ReviewRequest>> GetRequestHistoryAsync(int storeId, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        List<ReviewRequestEntity> entities = await _db.ReviewRequests
            .AsNoTracking()
            .Include(r => r.ReviewChannel)
            .Where(r => r.StoreId == storeId)
            .OrderByDescending(r => r.SentAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(e =>
        {
            ReviewRequest req = MapRequestToDomain(e);
            req.PlatformName = e.ReviewChannel?.PlatformName;
            return req;
        }).ToList();
    }

    public async Task<ReviewChannel> CreateChannelAsync(ReviewChannel channel,
        CancellationToken cancellationToken = default)
    {
        ReviewChannelEntity entity = new()
        {
            StoreId = channel.StoreId,
            PlatformName = channel.PlatformName,
            ReviewUrl = channel.ReviewUrl,
            SortOrder = channel.SortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ReviewChannels.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        channel.ReviewChannelId = entity.ReviewChannelId;
        return channel;
    }

    public async Task UpdateChannelAsync(int channelId, string platformName, string reviewUrl,
        int sortOrder, bool isActive, CancellationToken cancellationToken = default)
    {
        ReviewChannelEntity? entity = await _db.ReviewChannels
            .FirstOrDefaultAsync(r => r.ReviewChannelId == channelId, cancellationToken);

        if (entity is null) return;

        entity.PlatformName = platformName;
        entity.ReviewUrl = reviewUrl;
        entity.SortOrder = sortOrder;
        entity.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteChannelAsync(int channelId,
        CancellationToken cancellationToken = default)
    {
        ReviewChannelEntity? entity = await _db.ReviewChannels
            .FirstOrDefaultAsync(r => r.ReviewChannelId == channelId, cancellationToken);

        if (entity is null) return;

        _db.ReviewChannels.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ClearRequestHistoryAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        return await _db.ReviewRequests
            .Where(r => r.StoreId == storeId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static ReviewChannel MapChannelToDomain(ReviewChannelEntity entity)
    {
        return new ReviewChannel
        {
            ReviewChannelId = entity.ReviewChannelId,
            StoreId = entity.StoreId,
            PlatformName = entity.PlatformName,
            ReviewUrl = entity.ReviewUrl,
            SortOrder = entity.SortOrder,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }

    private static ReviewRequest MapRequestToDomain(ReviewRequestEntity entity)
    {
        return new ReviewRequest
        {
            ReviewRequestId = entity.ReviewRequestId,
            StoreId = entity.StoreId,
            CustomerId = entity.CustomerId,
            PhoneE164 = entity.PhoneE164,
            ReviewChannelId = entity.ReviewChannelId,
            TemplateId = entity.TemplateId,
            MessageBody = entity.MessageBody,
            TwilioSid = entity.TwilioSid,
            Status = entity.Status,
            ProviderStatus = entity.ProviderStatus,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            DeliveredAt = entity.DeliveredAt,
            SentAt = entity.SentAt
        };
    }
}
