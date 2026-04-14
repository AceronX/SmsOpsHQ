namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the ReviewChannels table.
public sealed class ReviewChannelEntity
{
    public int ReviewChannelId { get; set; }
    public int StoreId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public StoreEntity Store { get; set; } = null!;
}
