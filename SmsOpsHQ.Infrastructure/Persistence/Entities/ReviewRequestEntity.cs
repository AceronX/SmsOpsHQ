namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the ReviewRequests table.
public sealed class ReviewRequestEntity
{
    public int ReviewRequestId { get; set; }
    public int StoreId { get; set; }
    public int CustomerId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public int ReviewChannelId { get; set; }
    public int TemplateId { get; set; }
    public string MessageBody { get; set; } = string.Empty;
    public string? TwilioSid { get; set; }
    public string Status { get; set; } = "Accepted";
    public string? ProviderStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime SentAt { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
    public CustomerEntity Customer { get; set; } = null!;
    public ReviewChannelEntity ReviewChannel { get; set; } = null!;
    public TemplateEntity Template { get; set; } = null!;
}
