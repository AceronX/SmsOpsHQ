using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly IXpdSyncService _syncService;
    private readonly IXpdSyncScheduler _syncScheduler;
    private readonly IXpdConfigService _xpdConfig;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        IXpdSyncService syncService,
        IXpdSyncScheduler syncScheduler,
        IXpdConfigService xpdConfig,
        IConfiguration configuration,
        ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _syncScheduler = syncScheduler;
        _xpdConfig = xpdConfig;
        _configuration = configuration;
        _logger = logger;
    }

    // GET /api/sync/config
    // Returns the effective XPD config (overlay + appsettings fallback) the
    // sync service will actually use. Password is intentionally NOT returned --
    // we only flag whether one is set so the UI can show "*****" vs empty.
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

        XpdConfig effective = _xpdConfig.GetEffective();
        return Ok(new
        {
            sqlite_path = sqlitePath ?? "",
            xpd_path = effective.DatabasePath,
            mdw_path = effective.MdwPath,
            xpd_user = effective.User,
            xpd_password_set = !string.IsNullOrEmpty(effective.Password),
            overlay_file_path = _xpdConfig.ConfigFilePath,
            overlay_file_exists = _xpdConfig.ConfigFileExists
        });
    }

    // POST /api/sync/config
    // Persists XPD path/credentials to %AppData%\SmsOpsHQ\xpd_config.json so
    // BOTH the manual sync AND the hourly auto-sync use them. Empty fields are
    // treated as "leave the previous overlay value alone" so the operator can
    // update one field at a time. HQ-only.
    [HttpPost("config")]
    public async Task<IActionResult> SaveSyncConfig(
        [FromBody] SyncRunOptions request,
        CancellationToken cancellationToken)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can change the sync configuration");

        if (request is null)
            return Problem(statusCode: 400, detail: "Request body is required");

        XpdConfig toSave = new()
        {
            DatabasePath = request.XpdPath ?? string.Empty,
            MdwPath = request.MdwPath ?? string.Empty,
            User = request.XpdUser ?? string.Empty,
            Password = request.XpdPassword ?? string.Empty
        };

        try
        {
            await _xpdConfig.SaveAsync(toSave, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save XPD config");
            return Problem(statusCode: 500, detail: "Could not save configuration: " + ex.Message);
        }

        XpdConfig effective = _xpdConfig.GetEffective();
        return Ok(new
        {
            success = true,
            xpd_path = effective.DatabasePath,
            mdw_path = effective.MdwPath,
            xpd_user = effective.User,
            xpd_password_set = !string.IsNullOrEmpty(effective.Password),
            overlay_file_path = _xpdConfig.ConfigFilePath
        });
    }

    // GET /api/sync/preflight
    // One-shot health snapshot the operator can trigger from the Settings page
    // before the first sync (or anytime sync mysteriously fails). Each item is
    // either OK or has an actionable hint -- no need to grep server logs.
    [HttpGet("preflight")]
    public async Task<IActionResult> GetPreflight(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        XpdConfig xpd = _xpdConfig.GetEffective();

        bool xpdExists = !string.IsNullOrWhiteSpace(xpd.DatabasePath) && System.IO.File.Exists(xpd.DatabasePath);
        long xpdSize = 0;
        DateTime? xpdModified = null;
        if (xpdExists)
        {
            try
            {
                FileInfo fi = new(xpd.DatabasePath);
                xpdSize = fi.Length;
                xpdModified = fi.LastWriteTime;
            }
            catch { /* best effort */ }
        }

        bool mdwExists = !string.IsNullOrWhiteSpace(xpd.MdwPath) && System.IO.File.Exists(xpd.MdwPath);

        string exportRaw = _configuration["Xpd:ExportScriptPath"] ?? "export_xpd_to_sql.vbs";
        string exportPath = Path.IsPathRooted(exportRaw)
            ? exportRaw
            : Path.Combine(AppContext.BaseDirectory, exportRaw);
        bool exportScriptExists = System.IO.File.Exists(exportPath);

        string cscriptPath = _configuration["Xpd:CscriptPath"] ?? @"C:\Windows\SysWOW64\cscript.exe";
        bool cscriptExists = System.IO.File.Exists(cscriptPath);

        int storeCount = await db.Stores.CountAsync(cancellationToken);
        int twilioNumberCount = await db.TwilioNumbers.CountAsync(cancellationToken);

        SyncStatus status = _syncService.GetSyncStatus();
        XpdSyncSchedulerStatus scheduler = _syncScheduler.GetStatus();

        // Build a list of human-readable warnings so the UI can show
        // ONE banner for the whole tab instead of grepping fields.
        List<string> blockers = new();
        if (!xpdExists)
            blockers.Add($"XPD file not found at {xpd.DatabasePath}. Click Browse and pick the .XPD for this store.");
        if (!mdwExists)
            blockers.Add($"MDW workgroup file not found at {xpd.MdwPath}. Click Browse and pick the .mdw for this store.");
        if (!exportScriptExists)
            blockers.Add("export_xpd_to_sql.vbs is missing from the API folder. Reinstall the app.");
        if (!cscriptExists)
            blockers.Add($"cscript.exe not found at {cscriptPath}. Adjust Xpd:CscriptPath in api\\appsettings.json.");
        if (storeCount == 0)
            blockers.Add("No store has been created yet. Open Settings → Phone Numbers → Add store.");

        return Ok(new
        {
            ready = blockers.Count == 0,
            blockers,
            xpd_path = xpd.DatabasePath,
            xpd_file_exists = xpdExists,
            xpd_file_size = xpdSize,
            xpd_file_modified = xpdModified?.ToString("o"),
            mdw_path = xpd.MdwPath,
            mdw_file_exists = mdwExists,
            export_script_path = exportPath,
            export_script_exists = exportScriptExists,
            cscript_path = cscriptPath,
            cscript_exists = cscriptExists,
            store_count = storeCount,
            twilio_number_count = twilioNumberCount,
            last_sync = status.LastSync?.ToString("o"),
            last_sync_error = status.Error,
            scheduler_running = scheduler.Running,
            scheduler_next_run = scheduler.NextRunTime,
            scheduler_last_run = scheduler.LastRunTime,
            scheduler_last_success = scheduler.LastRunSuccess,
            scheduler_last_error = scheduler.LastRunError
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

    // GET /api/sync/scheduler/status
    // Returns whether the hourly auto-sync is running, the next/last run times,
    // and success/failure counts. Any authenticated user can read this so the
    // master console + each store's Settings page can surface health.
    [HttpGet("scheduler/status")]
    public IActionResult GetSchedulerStatus()
    {
        return Ok(_syncScheduler.GetStatus());
    }

    // POST /api/sync/scheduler/start
    // Turns the recurring timer on. Idempotent. HQ-only -- store users
    // shouldn't toggle automatic ops behavior for their store.
    [HttpPost("scheduler/start")]
    public IActionResult StartScheduler()
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can control the sync scheduler");

        _syncScheduler.Start();
        return Ok(_syncScheduler.GetStatus());
    }

    // POST /api/sync/scheduler/stop
    // Turns the recurring timer off. An in-flight sync finishes naturally.
    [HttpPost("scheduler/stop")]
    public IActionResult StopScheduler()
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can control the sync scheduler");

        _syncScheduler.Stop();
        return Ok(_syncScheduler.GetStatus());
    }

    // POST /api/sync/scheduler/run-now
    // Fires a sync immediately, outside the recurring schedule. Useful for the
    // operator "force refresh" button without disturbing the timer cadence.
    [HttpPost("scheduler/run-now")]
    public IActionResult RunSchedulerNow()
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Only HQ users can trigger sync");

        // Fire and forget so the HTTP request returns immediately. Caller
        // polls /api/sync/progress or /api/sync/scheduler/status for results.
        _ = Task.Run(async () =>
        {
            try
            {
                SyncResult result = await _syncScheduler.RunNowAsync();
                _logger.LogInformation("Manual scheduler run-now completed: {Success}", result.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual scheduler run-now failed");
            }
        });

        return Accepted(new { message = "Sync started" });
    }
}
