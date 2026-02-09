namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the Templates table.
public sealed class TemplateEntity
{
    public int TemplateId { get; set; }
    public int StoreId { get; set; }
    public int? CreatedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Hotkey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
    public UserEntity? CreatedByUser { get; set; }
}
