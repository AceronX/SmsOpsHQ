using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Review entities (channels + requests).
public interface IReviewRepository
{
    // Get active review channels for a store, sorted by SortOrder.
    Task<List<ReviewChannel>> GetActiveChannelsAsync(int storeId,
        CancellationToken cancellationToken = default);

    // Get all review channels for a store (active and inactive).
    Task<List<ReviewChannel>> GetChannelsAsync(int storeId,
        CancellationToken cancellationToken = default);

    // Get the last review request sent to a specific customer at a store (for channel rotation).
    Task<ReviewRequest?> GetLastRequestForCustomerAsync(int storeId, int customerId,
        CancellationToken cancellationToken = default);

    // Count how many times a specific template was used for a customer at a store (for template rotation).
    Task<int> GetTemplateUsageCountAsync(int storeId, int customerId, int templateId,
        CancellationToken cancellationToken = default);

    // Record a new review request.
    Task<ReviewRequest> CreateRequestAsync(ReviewRequest request,
        CancellationToken cancellationToken = default);

    // Get paginated review request history for a store.
    Task<List<ReviewRequest>> GetRequestHistoryAsync(int storeId, int skip, int take,
        CancellationToken cancellationToken = default);

    // Create a new review channel.
    Task<ReviewChannel> CreateChannelAsync(ReviewChannel channel,
        CancellationToken cancellationToken = default);

    // Update an existing review channel.
    Task UpdateChannelAsync(int channelId, string platformName, string reviewUrl,
        int sortOrder, bool isActive, CancellationToken cancellationToken = default);

    // Delete a review channel by ID.
    Task DeleteChannelAsync(int channelId,
        CancellationToken cancellationToken = default);

    // Delete all review request history for a store.
    Task<int> ClearRequestHistoryAsync(int storeId,
        CancellationToken cancellationToken = default);
}
