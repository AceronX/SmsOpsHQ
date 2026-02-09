namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the SMS_Unsubscribed table.
// Phones that have opted out of reminders via STOP or manual unsubscribe.
public sealed class SmsUnsubscribedEntity
{
    public int Id { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
