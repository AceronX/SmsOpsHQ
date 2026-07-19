namespace SmsOpsHQ.Core.Entities;

public sealed class LateTicketPull
{
    public int LateTicketPullId { get; set; }
    public int StoreId { get; set; }
    public int TicketKey { get; set; }
    public int CustomerKey { get; set; }
    public string? Reason { get; set; }
    public int PulledByUserId { get; set; }
    public DateTime PulledAtUtc { get; set; }
}
