using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly IXpdSyncService _syncService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SyncController> _logger;

    public SyncController(IXpdSyncService syncService, IConfiguration configuration, ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("config")]
    public IActionResult GetSyncConfig()
    {
        string? sqlitePath = _configuration.GetSection("Database")["SqlitePath"];
        if (string.IsNullOrWhiteSpace(sqlitePath))
        {
            string? connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                const string prefix = "Data Source=";
                if (connectionString.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    sqlitePath = connectionString.TrimStart().Substring(prefix.Length).Trim();
                else
                    sqlitePath = connectionString;
            }
        }
        else
        {
            sqlitePath = sqlitePath.Trim();
        }

        IConfigurationSection xpd = _configuration.GetSection("Xpd");
        return Ok(new
        {
            sqlite_path = sqlitePath ?? "",
            xpd_path = xpd["DatabasePath"] ?? "",
            mdw_path = xpd["MdwPath"] ?? "",
            xpd_user = xpd["User"] ?? ""
        });
    }

    [HttpPost("full")]
    public IActionResult TriggerFullSync([FromBody] SyncRunOptions? options = null)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can trigger sync");

        if (!_syncService.TryMarkSyncStarting())
        {
            return Ok(new
            {
                success = false,
                message = "Sync already in progress"
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                SyncResult result = await _syncService.FullSyncAsync(options);
                _logger.LogInformation("Background sync completed: {Success}", result.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background sync failed");
            }
        });

        return Ok(new
        {
            success = true,
            message = "Sync started in background"
        });
    }

    [HttpPost("full-blocking")]
    public async Task<IActionResult> TriggerFullSyncBlocking([FromBody] SyncRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can trigger sync");

        SyncProgress progress = _syncService.GetProgress();
        if (progress.InProgress)
        {
            return Ok(new
            {
                success = false,
                message = "Sync already in progress"
            });
        }

        SyncResult result = await _syncService.FullSyncAsync(options, cancellationToken);

        return Ok(new
        {
            success = result.Success,
            error = result.Error,
            started_at = result.StartedAt?.ToString("o"),
            completed_at = result.CompletedAt?.ToString("o"),
            duration_seconds = result.DurationSeconds,
            sqlite_before = result.SqliteBefore,
            sqlite_after = result.SqliteAfter,
                synced = new
                {
                    customers = result.Synced.Customers,
                    tickets = result.Synced.Tickets,
                    items = result.Synced.Items,
                    payments = result.Synced.Payments,
                    phone_index = result.Synced.PhoneIndex
                }
        });
    }

    // GET /api/sync/status
    // Returns last sync time, stats, and whether a sync is running.
    [HttpGet("status")]
    public IActionResult GetSyncStatus()
    {
        SyncStatus status = _syncService.GetSyncStatus();

        return Ok(new
        {
            last_sync = status.LastSync?.ToString("o"),
            sqlite_counts = status.SqliteCounts,
            last_sync_stats = status.LastSyncStats,
            sync_in_progress = status.SyncInProgress,
            error = status.Error
        });
    }

    // GET /api/sync/progress
    // Returns current sync progress (stage, count, percent) for progress bars.
    [HttpGet("progress")]
    public IActionResult GetSyncProgress()
    {
        SyncProgress progress = _syncService.GetProgress();

        return Ok(new
        {
            in_progress = progress.InProgress,
            stage = progress.Stage,
            current = progress.Current,
            total = progress.Total,
            percent = progress.Percent,
            message = progress.Message
        });
    }

    // GET /api/sync/counts
    // Returns row counts for all SQLite XPD mirror tables.
    [HttpGet("counts")]
    public async Task<IActionResult> GetSqliteCounts(CancellationToken cancellationToken)
    {
        Dictionary<string, int> counts = await _syncService.GetSqliteCountsAsync(cancellationToken);
        return Ok(counts);
    }
}
