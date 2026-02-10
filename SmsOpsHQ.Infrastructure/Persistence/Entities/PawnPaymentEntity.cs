namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity for the PawnPayments table (payments synced from XPawn).
public sealed class PawnPaymentEntity
{
    public int Key { get; set; }
    public int TicketKey { get; set; }
    public string? PaymentDate { get; set; }
    public int? PawnPmtType { get; set; }
    public string? PaymentStatus { get; set; }
    public double? TotalDueAmount { get; set; }
    public double? NetDueAmount { get; set; }
    public double? NetPaymentAmount { get; set; }
    public double? Cash { get; set; }
    public double? Check { get; set; }
    public double? CreditCard { get; set; }
    public double? DebitCard { get; set; }
    public double? InterestChargePaid { get; set; }
    public double? ServiceChargePaid { get; set; }
    public double? PrincipalPaid { get; set; }
    public double? NewCurrentBalance { get; set; }
    public string? NewDueDate { get; set; }
    public string? OldDueDate { get; set; }
    public string? OperatorInitials { get; set; }
    public string? Method { get; set; }
    public string? Note { get; set; }
    public string? SyncedAt { get; set; }
}
