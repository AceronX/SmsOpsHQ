namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity for the Items table (pawn items synced from XPawn).
public sealed class ItemEntity
{
    public int Key { get; set; }
    public int TicketKey { get; set; }
    public string? PrintedDetail { get; set; }
    public string? CategoryCode { get; set; }
    public string? SerialNo { get; set; }
    public double? Cost { get; set; }
    public string? ItemStatus { get; set; }
    public string? Notes { get; set; }
    public string? Mfg { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Weight { get; set; }
    public string? Karat { get; set; }
    public string? SyncedAt { get; set; }
}
