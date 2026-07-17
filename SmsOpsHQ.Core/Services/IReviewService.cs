using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for sending review request SMS with channel + template rotation.
public interface IReviewService
{
    // Send a review request to a customer. Handles channel rotation, template selection,
    // message rendering, Twilio send, and request recording.
    Task<ReviewRequestDto> SendReviewRequestAsync(int storeId, string customerPhone,
        int? twilioNumberId = null,
        CancellationToken cancellationToken = default);

    Task<ReviewReadinessDto> GetReadinessAsync(int storeId, int? twilioNumberId = null,
        CancellationToken cancellationToken = default);
}
