namespace SmsOpsHQ.Core.Entities;

// Domain representation of a pawn ticket synced from XPawn.
// Used by ITicketRepository, PawnCalculator, and customer context endpoints.
public sealed class Ticket
{
    // Pawn system primary key
    public int Key { get; set; }

    // FK to Customer
    public int CustomerKey { get; set; }

    public int? TransNo { get; set; }

    // 0 = buy, non-zero = pawn/loan
    public int? Type { get; set; }

    // 1 = active, 0 = closed
    public int? Active { get; set; }

    public double? Amount { get; set; }
    public double? CurrentBalance { get; set; }

    // Dates stored as ISO-8601 strings from XPawn
    public string? IssueDate { get; set; }
    public string? DueDate { get; set; }
    public string? DateClosed { get; set; }

    // "CPU" = customer pickup, "PFX-" prefix = forfeited
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
}
