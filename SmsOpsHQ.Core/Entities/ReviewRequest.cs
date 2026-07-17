namespace SmsOpsHQ.Core.Entities;

// Tracks every sent review request SMS. Maps to the ReviewRequests table.
public sealed class ReviewRequest
{
    public int ReviewRequestId { get; set; }
    public int StoreId { get; set; }
    public int CustomerId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public int ReviewChannelId { get; set; }

    // FK to Templates table
    public int TemplateId { get; set; }

    // Rendered message body that was actually sent
    public string MessageBody { get; set; } = string.Empty;

    public string? TwilioSid { get; set; }

    // Denormalized from ReviewChannel for display purposes
    public string? PlatformName { get; set; }

    // "Accepted", "Delivered", "Undelivered", "Failed", or "Mock"
    public string Status { get; set; } = "Accepted";

    public string? ProviderStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public DateTime SentAt { get; set; }
}
