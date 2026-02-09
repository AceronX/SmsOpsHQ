namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the SMS_Reminders table.
// Tracks every reminder sent (or attempted) to a customer.
public sealed class SmsReminderEntity
{
    public int Id { get; set; }
    public int? TicketKey { get; set; }
    public int? CustomerKey { get; set; }
    public string? DueDate { get; set; }
    public string? Phone { get; set; }
    public string? ReminderType { get; set; }
    public string? Message { get; set; }
    public int Status { get; set; }
    public string? TwilioSid { get; set; }
    public string? ErrorMessage { get; set; }
    public int? SentByUserId { get; set; }
    public int? StoreId { get; set; }
    public string? StorePhone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
