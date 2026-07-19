namespace SmsOpsHQ.Core.Entities;

public sealed class CustomerAppNote
{
    public int CustomerAppNoteId { get; set; }
    public int StoreId { get; set; }
    public int CustomerKey { get; set; }
    public string Content { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
