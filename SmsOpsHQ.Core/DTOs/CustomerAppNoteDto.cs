namespace SmsOpsHQ.Core.DTOs;

public sealed class CustomerAppNoteDto
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

public sealed class CreateCustomerAppNoteRequest
{
    public string Content { get; set; } = string.Empty;
}
