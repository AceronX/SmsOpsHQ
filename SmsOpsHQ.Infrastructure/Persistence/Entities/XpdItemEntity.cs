namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the XPD_Items mirror table.
// Synced from XPawn MS Access database via VBScript streaming.
public sealed class XpdItemEntity
{
    public int Key { get; set; }
    public int TicketKey { get; set; }
    public string? PrintedDetail { get; set; }
    public string? CategoryCode { get; set; }
    public string? SerialNo { get; set; }
    public double? Cost { get; set; }
    public string? ItemStatus { get; set; }
    public string? Notes { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Weight { get; set; }
    public string? Metal { get; set; }
    public string? SyncedAt { get; set; }
}
