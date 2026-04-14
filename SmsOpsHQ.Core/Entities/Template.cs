namespace SmsOpsHQ.Core.Entities;

// Message template for quick replies. Maps to the Templates table.
public sealed class Template
{
    public int TemplateId { get; set; }

    // FK to Stores.StoreId
    public int StoreId { get; set; }

    // FK to Users.UserId — who created this template
    public int? CreatedByUserId { get; set; }

    // Template display name
    public string Name { get; set; } = string.Empty;

    // Template message body text
    public string Body { get; set; } = string.Empty;

    // Keyboard shortcut key, e.g. "F1"
    public string? Hotkey { get; set; }

    // "General" or "Review"
    public string Category { get; set; } = "General";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
