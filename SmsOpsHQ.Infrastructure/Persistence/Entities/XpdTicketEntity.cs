namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the XPD_Tickets mirror table.
// Synced from XPawn MS Access database via VBScript streaming.
public sealed class XpdTicketEntity
{
    public int Key { get; set; }
    public int CustomerKey { get; set; }
    public int? TransNo { get; set; }
    public int? Type { get; set; }
    public int? Active { get; set; }
    public double? Amount { get; set; }
    public double? CurrentBalance { get; set; }
    public string? IssueDate { get; set; }
    public string? DueDate { get; set; }
    public string? DateClosed { get; set; }
    public string? HowClosed { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
    public string? Item { get; set; }
    public string? OperatorInitials { get; set; }
    public int? GunTicket { get; set; }
    public int? LostTicket { get; set; }
    public string? PaidTillDate { get; set; }
    public string? LastDate { get; set; }
    public double? ChargesDue { get; set; }
    public double? StandardCharges { get; set; }
    public double? StandardPU { get; set; }
    public double? FullTermPU { get; set; }
    public double? FulltermRenew { get; set; }
    public string? SyncedAt { get; set; }
}
