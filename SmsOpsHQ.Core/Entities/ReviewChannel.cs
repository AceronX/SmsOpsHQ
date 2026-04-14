namespace SmsOpsHQ.Core.Entities;

// Review platform channel (Google, Yelp, etc.) for a store. Maps to the ReviewChannels table.
public sealed class ReviewChannel
{
    public int ReviewChannelId { get; set; }
    public int StoreId { get; set; }

    // Platform display name: "Google", "Yelp", "BBB", "Facebook", "Trustpilot"
    public string PlatformName { get; set; } = string.Empty;

    // Full URL to the store's review page on this platform
    public string ReviewUrl { get; set; } = string.Empty;

    // Controls rotation order
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
