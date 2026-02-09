namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the SMS_Excluded table.
// Phones that should never receive reminders.
public sealed class SmsExcludedEntity
{
    public int Id { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public int? ExcludedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
