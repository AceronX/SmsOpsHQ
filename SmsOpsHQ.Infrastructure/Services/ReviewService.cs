using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Infrastructure.Services;

// Sends review request SMS with automatic channel + template rotation.
public sealed class ReviewService : IReviewService
{
    private readonly IReviewRepository _reviewRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IStoreRepository _storeRepo;
    private readonly ITwilioService _twilioService;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IReviewRepository reviewRepo,
        ITemplateRepository templateRepo,
        ICustomerRepository customerRepo,
        IStoreRepository storeRepo,
        ITwilioService twilioService,
        ILogger<ReviewService> logger)
    {
        _reviewRepo = reviewRepo;
        _templateRepo = templateRepo;
        _customerRepo = customerRepo;
        _storeRepo = storeRepo;
        _twilioService = twilioService;
        _logger = logger;
    }

    public async Task<ReviewRequestDto> SendReviewRequestAsync(int storeId, string customerPhone,
        CancellationToken cancellationToken = default)
    {
        // 1. Normalize phone + find/create customer.
        string normalizedPhone = PhoneUtils.NormalizeToE164(customerPhone)
            ?? throw new InvalidOperationException($"Invalid phone number: {customerPhone}");
        Customer customer = await _customerRepo.FindOrCreateAsync(storeId, normalizedPhone, cancellationToken);

        // 2. Get store info.
        Store? store = await _storeRepo.GetByIdAsync(storeId, cancellationToken);
        if (store is null)
            throw new InvalidOperationException("Store not found.");

        string storeName = store.StoreName;

        // 3. Get active channels for this store.
        List<ReviewChannel> channels = await _reviewRepo.GetActiveChannelsAsync(storeId, cancellationToken);
        if (channels.Count == 0)
            throw new InvalidOperationException("No review channels configured for this store. Add channels in Settings > Reviews.");

        // 4. Channel rotation: pick next channel based on last request.
        ReviewRequest? lastRequest = await _reviewRepo.GetLastRequestForCustomerAsync(storeId, customer.CustomerId, cancellationToken);
        ReviewChannel selectedChannel;

        if (lastRequest is null)
        {
            selectedChannel = channels[0];
        }
        else
        {
            int lastIndex = channels.FindIndex(c => c.ReviewChannelId == lastRequest.ReviewChannelId);
            selectedChannel = lastIndex < 0 ? channels[0] : channels[(lastIndex + 1) % channels.Count];
        }

        // 5. Get review templates.
        List<Template> templates = await _templateRepo.GetByStoreAndCategoryAsync(storeId, "Review", cancellationToken);
        if (templates.Count == 0)
            throw new InvalidOperationException("No review templates found. Create templates with category 'Review'.");

        // 6. Template rotation: pick least-used template for this customer.
        Template selectedTemplate = templates[0];
        int minUsage = int.MaxValue;

        foreach (Template t in templates)
        {
            int usage = await _reviewRepo.GetTemplateUsageCountAsync(storeId, customer.CustomerId, t.TemplateId, cancellationToken);
            if (usage < minUsage)
            {
                minUsage = usage;
                selectedTemplate = t;
            }
        }

        // 7. Render message: replace {link} with channel URL, {store} with store name.
        string messageBody = selectedTemplate.Body
            .Replace("{link}", selectedChannel.ReviewUrl)
            .Replace("{store}", storeName);

        // 8. Get store's default Twilio number.
        string? storePhone = await _storeRepo.GetDefaultNumberAsync(storeId, cancellationToken);
        if (string.IsNullOrEmpty(storePhone))
            throw new InvalidOperationException("No default phone number configured for this store.");

        // 9. Send via Twilio.
        TwilioSendResult twilioResult = await _twilioService.SendSmsAsync(
            storePhone, normalizedPhone, messageBody, cancellationToken: cancellationToken);

        string status = twilioResult.Success ? "Sent" : "Failed";

        // 10. Record the review request.
        ReviewRequest reviewRequest = new()
        {
            StoreId = storeId,
            CustomerId = customer.CustomerId,
            PhoneE164 = normalizedPhone,
            ReviewChannelId = selectedChannel.ReviewChannelId,
            TemplateId = selectedTemplate.TemplateId,
            MessageBody = messageBody,
            TwilioSid = twilioResult.TwilioSid,
            Status = status,
            SentAt = DateTime.UtcNow
        };

        await _reviewRepo.CreateRequestAsync(reviewRequest, cancellationToken);

        _logger.LogInformation(
            "Review request sent: Store={StoreId} Phone={Phone} Channel={Channel} Status={Status}",
            storeId, normalizedPhone, selectedChannel.PlatformName, status);

        if (!twilioResult.Success)
            throw new InvalidOperationException($"SMS send failed: {twilioResult.ErrorMessage}");

        // 11. Return result.
        return new ReviewRequestDto
        {
            ReviewRequestId = reviewRequest.ReviewRequestId,
            PhoneE164 = normalizedPhone,
            PlatformName = selectedChannel.PlatformName,
            MessageBody = messageBody,
            SentAt = reviewRequest.SentAt,
            Status = status
        };
    }
}
