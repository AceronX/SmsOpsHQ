namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// Single-row table (StateId = 1): watermark for XPD ticket keys processed by review automation.
public sealed class ReviewAutomationStateEntity
{
    public int StateId { get; set; } = 1;

    // Null = not bootstrapped yet (first run will set to current MAX(Key) without sending SMS).
    public int? LastMaxTicketKey { get; set; }
}
