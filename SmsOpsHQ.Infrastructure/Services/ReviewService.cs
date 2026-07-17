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
    private readonly IOutboundNumberResolver _numberResolver;
    private readonly ITwilioService _twilioService;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IReviewRepository reviewRepo,
        ITemplateRepository templateRepo,
        ICustomerRepository customerRepo,
        IStoreRepository storeRepo,
        IOutboundNumberResolver numberResolver,
        ITwilioService twilioService,
        ILogger<ReviewService> logger)
    {
        _reviewRepo = reviewRepo;
        _templateRepo = templateRepo;
        _customerRepo = customerRepo;
        _storeRepo = storeRepo;
        _numberResolver = numberResolver;
        _twilioService = twilioService;
        _logger = logger;
    }

    public async Task<ReviewReadinessDto> GetReadinessAsync(
        int storeId,
        int? twilioNumberId = null,
        CancellationToken cancellationToken = default)
    {
        ReviewReadinessDto result = new();

        Store? store = await _storeRepo.GetByIdAsync(storeId, cancellationToken);
        AddCheck(result, "store", "Store", store is not null,
            store is null ? "Store was not found." : $"Store '{store.StoreName}' exists.");

        try
        {
            OutboundNumberResolution sender = await _numberResolver.ResolveAsync(
                storeId, twilioNumberId, cancellationToken);
            result.TwilioNumberId = sender.TwilioNumberId;
            result.FromPhoneE164 = sender.PhoneE164;
            AddCheck(result, "sender", "Sender number", true,
                "The selected/default Twilio number exists, belongs to this store, and is active.");
        }
        catch (OutboundNumberValidationException ex)
        {
            AddCheck(result, "sender", "Sender number", false, ex.Message);
        }

        AddCheck(result, "twilio", "Twilio live mode", !_twilioService.IsMockMode,
            _twilioService.IsMockMode
                ? "Twilio is in mock mode. Configure a live Account SID and Auth Token."
                : "Twilio credentials are configured for live sending.");

        List<ReviewChannel> channels = await _reviewRepo.GetActiveChannelsAsync(storeId, cancellationToken);
        AddCheck(result, "channel", "Active review channel", channels.Count > 0,
            channels.Count > 0
                ? $"{channels.Count} active review channel(s) available."
                : "No active review channel exists. Add one in Settings > Reviews.");

        List<Template> templates = await _templateRepo.GetByStoreAndCategoryAsync(
            storeId, "Review", cancellationToken);
        AddCheck(result, "template", "Review template", templates.Count > 0,
            templates.Count > 0
                ? $"{templates.Count} Review template(s) available."
                : "No Review template exists. Create a template with category 'Review'.");

        result.Ready = result.Checks.All(c => c.Passed);
        return result;
    }

    public async Task<ReviewRequestDto> SendReviewRequestAsync(
        int storeId,
        string customerPhone,
        int? twilioNumberId = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedPhone = PhoneUtils.NormalizeToE164(customerPhone)
            ?? throw new InvalidOperationException("Customer phone is not a valid US phone number.");

        Store? store = await _storeRepo.GetByIdAsync(storeId, cancellationToken);
        if (store is null)
            throw new InvalidOperationException("Store not found.");

        List<ReviewChannel> channels = await _reviewRepo.GetActiveChannelsAsync(storeId, cancellationToken);
        if (channels.Count == 0)
            throw new InvalidOperationException("No review channels configured for this store. Add channels in Settings > Reviews.");

        List<Template> templates = await _templateRepo.GetByStoreAndCategoryAsync(
            storeId, "Review", cancellationToken);
        if (templates.Count == 0)
            throw new InvalidOperationException("No review templates found. Create templates with category 'Review'.");

        OutboundNumberResolution sender = await _numberResolver.ResolveAsync(
            storeId, twilioNumberId, cancellationToken);

        Customer customer = await _customerRepo.FindOrCreateAsync(
            storeId, normalizedPhone, cancellationToken);

        ReviewChannel selectedChannel = await SelectChannelAsync(
            storeId, customer.CustomerId, channels, cancellationToken);
        Template selectedTemplate = await SelectTemplateAsync(
            storeId, customer.CustomerId, templates, cancellationToken);

        string messageBody = selectedTemplate.Body
            .Replace("{link}", selectedChannel.ReviewUrl)
            .Replace("{store}", store.StoreName);

        TwilioSendResult twilioResult = await _twilioService.SendSmsAsync(
            sender.PhoneE164,
            normalizedPhone,
            messageBody,
            cancellationToken: cancellationToken);

        string status = twilioResult.IsMock
            ? "Mock"
            : twilioResult.Success ? "Accepted" : "Failed";
        string providerStatus = string.IsNullOrWhiteSpace(twilioResult.Status)
            ? status
            : twilioResult.Status;

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
            ProviderStatus = providerStatus,
            ErrorCode = twilioResult.ErrorCode,
            ErrorMessage = twilioResult.ErrorMessage,
            SentAt = DateTime.UtcNow
        };

        await _reviewRepo.CreateRequestAsync(reviewRequest, cancellationToken);
        ReviewRequestDto response = MapToDto(reviewRequest, selectedChannel.PlatformName);

        _logger.LogInformation(
            "Review request attempt recorded: store={StoreId} customerPhone={Phone} senderNumberId={SenderNumberId} channel={Channel} status={Status} providerStatus={ProviderStatus}",
            storeId,
            RedactPhone(normalizedPhone),
            sender.TwilioNumberId,
            selectedChannel.PlatformName,
            status,
            providerStatus);

        if (twilioResult.IsMock)
        {
            throw new OutboundSendException(
                twilioResult.ErrorMessage ?? "Twilio is in mock mode; the review request was not delivered.",
                response);
        }

        if (!twilioResult.Success)
        {
            throw new OutboundSendException(
                twilioResult.ErrorMessage ?? "Twilio rejected the review request.",
                response);
        }

        return response;
    }

    private async Task<ReviewChannel> SelectChannelAsync(
        int storeId,
        int customerId,
        List<ReviewChannel> channels,
        CancellationToken cancellationToken)
    {
        ReviewRequest? lastRequest = await _reviewRepo.GetLastRequestForCustomerAsync(
            storeId, customerId, cancellationToken);
        if (lastRequest is null)
            return channels[0];

        int lastIndex = channels.FindIndex(c => c.ReviewChannelId == lastRequest.ReviewChannelId);
        return lastIndex < 0 ? channels[0] : channels[(lastIndex + 1) % channels.Count];
    }

    private async Task<Template> SelectTemplateAsync(
        int storeId,
        int customerId,
        List<Template> templates,
        CancellationToken cancellationToken)
    {
        Template selected = templates[0];
        int minUsage = int.MaxValue;
        foreach (Template template in templates)
        {
            int usage = await _reviewRepo.GetTemplateUsageCountAsync(
                storeId, customerId, template.TemplateId, cancellationToken);
            if (usage < minUsage)
            {
                minUsage = usage;
                selected = template;
            }
        }

        return selected;
    }

    private static ReviewRequestDto MapToDto(ReviewRequest request, string platformName)
    {
        return new ReviewRequestDto
        {
            ReviewRequestId = request.ReviewRequestId,
            PhoneE164 = request.PhoneE164,
            PlatformName = platformName,
            MessageBody = request.MessageBody,
            SentAt = request.SentAt,
            Status = request.Status,
            TwilioSid = request.TwilioSid,
            ProviderStatus = request.ProviderStatus,
            IsMock = string.Equals(request.Status, "Mock", StringComparison.OrdinalIgnoreCase),
            ErrorCode = request.ErrorCode,
            ErrorMessage = request.ErrorMessage,
            DeliveredAt = request.DeliveredAt
        };
    }

    private static void AddCheck(
        ReviewReadinessDto result,
        string code,
        string label,
        bool passed,
        string message)
    {
        result.Checks.Add(new ReviewReadinessCheckDto
        {
            Code = code,
            Label = label,
            Passed = passed,
            Message = message
        });
    }

    private static string RedactPhone(string phone) =>
        phone.Length <= 4 ? "****" : $"***{phone[^4..]}";
}
