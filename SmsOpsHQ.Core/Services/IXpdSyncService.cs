using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for syncing XPD (MS Access) pawn data to the local SQLite database.
// The sync exports via VBScript (cscript.exe), batch-upserts into Customers, Tickets, Items, PawnPayments, then rebuilds the CustomerPhones index for fast lookup.
public interface IXpdSyncService
{
    // Mark sync as starting and set initial progress (0%) so the UI shows progress immediately. Returns false if sync already in progress.
    bool TryMarkSyncStarting();

    // Perform a full sync of all pawn tables. Optional overrides for this run (XPD path, MDW, credentials).
    Task<SyncResult> FullSyncAsync(SyncRunOptions? overrides = null, CancellationToken cancellationToken = default);

    // Get progress of a currently-running sync (stage, count, percent).
    SyncProgress GetProgress();

    // Get overall sync status (last sync time, stats, whether currently running).
    SyncStatus GetSyncStatus();

    // Get row counts for all synced SQLite tables.
    Task<Dictionary<string, int>> GetSqliteCountsAsync(CancellationToken cancellationToken = default);
}

// Result returned after a full sync completes.
public sealed class SyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double DurationSeconds { get; set; }
    public Dictionary<string, int> SqliteBefore { get; set; } = new();
    public Dictionary<string, int> SqliteAfter { get; set; } = new();
    public SyncCounts Synced { get; set; } = new();
}

// Breakdown of rows synced per table.
public sealed class SyncCounts
{
    public int Customers { get; set; }
    public int Tickets { get; set; }
    public int Items { get; set; }
    public int Payments { get; set; }
    public int PhoneIndex { get; set; }
}

// Live progress of the current sync operation.
public sealed class SyncProgress
{
    public bool InProgress { get; set; }
    public string Stage { get; set; } = string.Empty;
    public int Current { get; set; }
    public int Total { get; set; }
    public int Percent { get; set; }
    public string Message { get; set; } = string.Empty;
}

// Status snapshot of the sync service.
public sealed class SyncStatus
{
    public DateTime? LastSync { get; set; }
    public Dictionary<string, int> SqliteCounts { get; set; } = new();
    public Dictionary<string, object> LastSyncStats { get; set; } = new();
    public bool SyncInProgress { get; set; }
    public string? Error { get; set; }
}
