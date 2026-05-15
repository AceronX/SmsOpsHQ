using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// XPD to SQLite sync service. Uses export_xpd_to_sql.vbs to export Access
// to a SQL file, then executes it (Customers, Tickets, Items, PawnPayments), then rebuilds CustomerPhones index.
public sealed class XpdSyncService : IXpdSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<XpdSyncService> _logger;
    private readonly IMemoryCache _cache;

    private readonly string _xpdPath;
    private readonly string _mdwPath;
    private readonly string _xpdUser;
    private readonly string _xpdPassword;
    private readonly string _exportScriptPath;
    private readonly string _cscriptPath;

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private volatile SyncRunOptions? _currentRunOverrides;

    private DateTime? _lastSync;
    private Dictionary<string, object> _lastSyncStats = new();
    private volatile bool _syncInProgress;
    private string? _lastError;
    private const string IdentityNegativeCachePrefix = "identity_neg:";

    private readonly object _progressLock = new();
    private SyncProgress _currentProgress = new()
    {
        Stage = string.Empty,
        Message = string.Empty
    };

    // Progress update intervals
    private const int ImportProgressInterval = 500;
    private const int PhoneIndexProgressInterval = 1000;

    // Progress stage percentages
    private const int StageInitPercent = 2;
    private const int StageExportPercent = 10;
    private const int StageImportStartPercent = 10;
    private const int StageImportEndPercent = 80;
    private const int StagePhoneIndexStartPercent = 80;
    private const int StagePhoneIndexEndPercent = 100;

    // Table names for counting
    private static readonly string[] TableNames = { "Customers", "Tickets", "Items", "PawnPayments", "CustomerPhones" };

    public XpdSyncService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<XpdSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;

        IConfigurationSection xpdSection = configuration.GetSection("Xpd");

        _xpdPath = xpdSection["DatabasePath"] ?? @"C:\xpawndata\pitkin.XPD";
        _mdwPath = xpdSection["MdwPath"] ?? @"C:\xpawndata\XcelData.mdw";
        _xpdUser = xpdSection["User"] ?? "developer";
        _xpdPassword = xpdSection["Password"] ?? "Hollerith89";
        string exportRaw = xpdSection["ExportScriptPath"] ?? "export_xpd_to_sql.vbs";
        _exportScriptPath = Path.IsPathRooted(exportRaw)
            ? exportRaw
            : Path.Combine(AppContext.BaseDirectory, exportRaw);
        _cscriptPath = xpdSection["CscriptPath"] ?? @"C:\Windows\SysWOW64\cscript.exe";
    }

    public bool TryMarkSyncStarting()
    {
        bool lockAcquired = _syncLock.Wait(0);
        if (!lockAcquired)
            return false;

        try
        {
            _syncInProgress = true;
            UpdateProgress("init", 0, 100, "Starting sync...", 0, 0);
            return true;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<SyncResult> FullSyncAsync(SyncRunOptions? overrides = null, CancellationToken cancellationToken = default)
    {
        bool lockAcquired = await _syncLock.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!lockAcquired)
        {
            return new SyncResult { Success = false, Error = "Sync already in progress" };
        }

        _currentRunOverrides = overrides;
        _lastError = null;
        try
        {
            _syncInProgress = true;
            DateTime startTime = DateTime.UtcNow;
            UpdateProgress("init", 0, 5, "Initializing sync...", 0, StageInitPercent);

            using IServiceScope scope = _scopeFactory.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Dictionary<string, int> sqliteBefore = await GetCountsFromDbAsync(db, cancellationToken);

            DbConnection dbConnection = db.Database.GetDbConnection();
            if (dbConnection.State != ConnectionState.Open)
                await dbConnection.OpenAsync(cancellationToken);
            SqliteConnection conn = (SqliteConnection)dbConnection;

            await ConfigureSqlitePerformanceAsync(conn, cancellationToken);
            await ValidatePrerequisitesAsync(db, cancellationToken);

            (int customersSynced, int ticketsSynced, int itemsSynced, int paymentsSynced) = 
                await RunSqlExportSyncAsync(conn, cancellationToken);

            UpdateProgress("phone_index", 0, 1, "Rebuilding phone index...", StagePhoneIndexStartPercent, StagePhoneIndexStartPercent);
            int phoneIndexCount = await RebuildPhoneIndexAsync(conn, db, cancellationToken);

            Dictionary<string, int> sqliteAfter = await GetCountsFromDbAsync(db, cancellationToken);

            DateTime endTime = DateTime.UtcNow;
            double durationSeconds = (endTime - startTime).TotalSeconds;

            _lastSync = endTime;
            _lastSyncStats = BuildSyncStats(customersSynced, ticketsSynced, itemsSynced, paymentsSynced, phoneIndexCount, durationSeconds);
            _lastError = null;

            string completeMsg = FormatCompletionMessage(durationSeconds, customersSynced, ticketsSynced, itemsSynced, paymentsSynced, phoneIndexCount);
            UpdateProgress("complete", 100, 100, completeMsg, 100, 100);

            _logger.LogInformation(
                "XPD sync completed in {Duration:F1}s: {Customers} customers, {Tickets} tickets, {Items} items, {Payments} payments, {Phones} phone entries",
                durationSeconds, customersSynced, ticketsSynced, itemsSynced, paymentsSynced, phoneIndexCount);

            return new SyncResult
            {
                Success = true,
                StartedAt = startTime,
                CompletedAt = endTime,
                DurationSeconds = durationSeconds,
                SqliteBefore = sqliteBefore,
                SqliteAfter = sqliteAfter,
                Synced = new SyncCounts
                {
                    Customers = customersSynced,
                    Tickets = ticketsSynced,
                    Items = itemsSynced,
                    Payments = paymentsSynced,
                    PhoneIndex = phoneIndexCount
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "XPD sync failed");
            _lastError = ex.Message;
            string safeMessage = SanitizeProgressMessage(ex.Message);
            UpdateProgress("error", 0, 100, string.IsNullOrEmpty(safeMessage) ? "Sync failed." : "Error: " + safeMessage);
            return new SyncResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _currentRunOverrides = null;
            _syncInProgress = false;
            _syncLock.Release();
        }
    }

    public SyncProgress GetProgress()
    {
        lock (_progressLock)
        {
            return new SyncProgress
            {
                InProgress = _syncInProgress,
                Stage = _currentProgress.Stage,
                Current = _currentProgress.Current,
                Total = _currentProgress.Total,
                Percent = _currentProgress.Percent,
                Message = _currentProgress.Message
            };
        }
    }

    public SyncStatus GetSyncStatus()
    {
        Dictionary<string, int> counts = new();
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            counts = GetCountsFromDbSync(db);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get SQLite counts for sync status");
        }

        string? error = _lastError ?? (_currentProgress.Stage == "error" ? _currentProgress.Message : null);
        return new SyncStatus
        {
            LastSync = _lastSync,
            SqliteCounts = counts,
            LastSyncStats = _lastSyncStats,
            SyncInProgress = _syncInProgress,
            Error = error
        };
    }

    public async Task<Dictionary<string, int>> GetSqliteCountsAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await GetCountsFromDbAsync(db, cancellationToken);
    }

    // ── Private Helper Methods ────────────────────────────────────────────

    private (string XpdPath, string MdwPath, string XpdUser, string XpdPassword) GetSyncCredentials()
    {
        return (
            _currentRunOverrides?.XpdPath ?? _xpdPath,
            _currentRunOverrides?.MdwPath ?? _mdwPath,
            _currentRunOverrides?.XpdUser ?? _xpdUser,
            _currentRunOverrides?.XpdPassword ?? _xpdPassword
        );
    }

    private async Task ConfigureSqlitePerformanceAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        using SqliteCommand pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ValidatePrerequisitesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (!File.Exists(_exportScriptPath))
            throw new InvalidOperationException($"Export script not found: {_exportScriptPath}. Ensure export_xpd_to_sql.vbs is deployed.");

        bool anyStore = await db.Stores.AnyAsync(cancellationToken);
        if (!anyStore)
        {
            string errorMsg = "Cannot sync: no store exists. Create a store in Settings → Phone Numbers first.";
            _lastError = errorMsg;
            _logger.LogWarning("XPD sync aborted: {Reason}", errorMsg);
            UpdateProgress("error", 0, 100, errorMsg);
            throw new InvalidOperationException(errorMsg);
        }
    }

    private async Task<(int Customers, int Tickets, int Items, int Payments)> RunSqlExportSyncAsync(
        SqliteConnection conn, CancellationToken cancellationToken)
    {
        var (xpdPath, mdwPath, xpdUser, xpdPassword) = GetSyncCredentials();
        string tempFile = Path.Combine(Path.GetTempPath(), "smsops_sync_" + Guid.NewGuid().ToString("N") + ".sql");
        
        try
        {
            UpdateProgress("export", 0, 100, "Exporting from Access...", 0, StageExportPercent);
            await RunExportScriptAsync(xpdPath, mdwPath, xpdUser, xpdPassword, tempFile, cancellationToken);

            if (!File.Exists(tempFile))
                throw new InvalidOperationException("Export script did not produce SQL file.");

            UpdateProgress("import", 0, 1, "Reading SQL file...", StageImportStartPercent, StageImportStartPercent);
            string[] lines = await File.ReadAllLinesAsync(tempFile, cancellationToken);

            return await ImportSqlFileAsync(conn, lines, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary SQL file: {File}", tempFile);
            }
        }
    }

    private async Task RunExportScriptAsync(string xpdPath, string mdwPath, string xpdUser, string xpdPassword, string outputFile, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _cscriptPath,
            Arguments = $"//nologo \"{_exportScriptPath}\" \"{xpdPath}\" \"{mdwPath}\" \"{xpdUser}\" \"{xpdPassword}\" \"{outputFile}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogInformation("VBScript output: {Stderr}", stderr.Trim());

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? "Export script failed." : stderr.Trim());
        }
    }

    private async Task<(int Customers, int Tickets, int Items, int Payments)> ImportSqlFileAsync(
        SqliteConnection conn, string[] lines, CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(conn, async (txn, ct) =>
        {
            var counts = await ExecuteSqlLinesAsync(conn, txn, lines, ct);

            if (counts.Payments == 0 && (counts.Customers > 0 || counts.Tickets > 0))
            {
                _logger.LogWarning(
                    "PawnPayments sync produced 0 rows while Customers={Customers}, Tickets={Tickets}, Items={Items}. Check VBScript stderr above for errors.",
                    counts.Customers, counts.Tickets, counts.Items);
            }

            return counts;
        }, cancellationToken);
    }

    private async Task<(int Customers, int Tickets, int Items, int Payments)> ExecuteSqlLinesAsync(
        SqliteConnection conn,
        SqliteTransaction txn,
        string[] lines,
        CancellationToken cancellationToken)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;

        int totalLines = lines.Length;
        int customers = 0, tickets = 0, items = 0, payments = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) ||
                line.Equals("BEGIN TRANSACTION;", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase))
                continue;

            // Count inserts
            if (line.StartsWith("INSERT INTO Customers", StringComparison.OrdinalIgnoreCase))
                customers++;
            else if (line.StartsWith("INSERT OR REPLACE INTO Tickets", StringComparison.OrdinalIgnoreCase))
                tickets++;
            else if (line.StartsWith("INSERT OR REPLACE INTO Items", StringComparison.OrdinalIgnoreCase))
                items++;
            else if (line.StartsWith("INSERT OR REPLACE INTO PawnPayments", StringComparison.OrdinalIgnoreCase))
                payments++;

            cmd.CommandText = line;
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            int processed = i + 1;
            if (processed % ImportProgressInterval == 0 || processed == totalLines)
            {
                string msg = $"Importing... {processed:N0} of {totalLines:N0} rows (Customers: {customers:N0}, Tickets: {tickets:N0}, Items: {items:N0}, Payments: {payments:N0})";
                UpdateProgress("import", processed, totalLines, msg, StageImportStartPercent, StageImportEndPercent);
            }
        }

        return (customers, tickets, items, payments);
    }

    // Rebuild CustomerPhones from ResPhone, BusPhone, and any numbers parsed from Notes so lookup by phone finds the customer.
    private async Task<int> RebuildPhoneIndexAsync(SqliteConnection conn, AppDbContext db, CancellationToken cancellationToken)
    {
        List<CustomerEntity> customers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerKey != null)
            .Select(c => new CustomerEntity { CustomerKey = c.CustomerKey, ResPhone = c.ResPhone, BusPhone = c.BusPhone, Notes = c.Notes })
            .ToListAsync(cancellationToken);

        List<TicketEntity> tickets = await db.Tickets
            .AsNoTracking()
            .Where(t => !string.IsNullOrWhiteSpace(t.Notes))
            .Select(t => new TicketEntity { CustomerKey = t.CustomerKey, Notes = t.Notes })
            .ToListAsync(cancellationToken);

        var ticketNotesByCustomer = tickets
            .GroupBy(t => t.CustomerKey)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList());

        HashSet<string> insertedNormalizedPhones = new(StringComparer.Ordinal);
        return await ExecuteInTransactionAsync(conn, async (txn, ct) =>
        {
            await ClearPhoneIndexAsync(conn, txn, ct);
            return await InsertPhoneIndexEntriesAsync(conn, txn, customers, ticketNotesByCustomer, insertedNormalizedPhones, ct);
        }, cancellationToken);
    }

    // Empty the CustomerPhones table before repopulating so we have a clean index.
    private async Task ClearPhoneIndexAsync(SqliteConnection conn, SqliteTransaction txn, CancellationToken cancellationToken)
    {
        using SqliteCommand delCmd = conn.CreateCommand();
        delCmd.Transaction = txn;
        delCmd.CommandText = "DELETE FROM CustomerPhones";
        await delCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // For each customer insert one row per phone: ResPhone, BusPhone (if different), and each number found in Notes.
    private async Task<int> InsertPhoneIndexEntriesAsync(
        SqliteConnection conn,
        SqliteTransaction txn,
        List<CustomerEntity> customers,
        Dictionary<int, List<string>> ticketNotesByCustomer,
        HashSet<string> insertedNormalizedPhones,
        CancellationToken cancellationToken)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = """
            INSERT OR IGNORE INTO CustomerPhones (CustomerKey, PhoneNormalized, PhoneOriginal, PhoneType, MatchType, IsDirect)
            VALUES ($custKey,$phoneNorm,$phoneOrig,$sourceField,$matchType,$isDirect)
            """;

        SqliteParameter pCustKey = cmd.Parameters.Add("$custKey", SqliteType.Integer);
        SqliteParameter pPhoneNorm = cmd.Parameters.Add("$phoneNorm", SqliteType.Text);
        SqliteParameter pPhoneOrig = cmd.Parameters.Add("$phoneOrig", SqliteType.Text);
        SqliteParameter pSourceField = cmd.Parameters.Add("$sourceField", SqliteType.Text);
        SqliteParameter pMatchType = cmd.Parameters.Add("$matchType", SqliteType.Text);
        SqliteParameter pIsDirect = cmd.Parameters.Add("$isDirect", SqliteType.Integer);
        cmd.Prepare();

        int count = 0;
        int total = customers.Count;

        for (int i = 0; i < customers.Count; i++)
        {
            CustomerEntity c = customers[i];
            int customerKey = c.CustomerKey!.Value;

            count += await ProcessPhoneAsync(cmd, pCustKey, pPhoneNorm, pPhoneOrig, pSourceField, pMatchType, pIsDirect, customerKey, c.ResPhone, "ResPhone", insertedNormalizedPhones, cancellationToken);

            if (!string.IsNullOrWhiteSpace(c.BusPhone) && c.BusPhone != c.ResPhone)
            {
                count += await ProcessPhoneAsync(cmd, pCustKey, pPhoneNorm, pPhoneOrig, pSourceField, pMatchType, pIsDirect, customerKey, c.BusPhone, "BusPhone", insertedNormalizedPhones, cancellationToken);
            }

            foreach (string notePhone in PhoneUtils.ExtractPhonesFromText(c.Notes))
            {
                count += await ProcessPhoneAsync(cmd, pCustKey, pPhoneNorm, pPhoneOrig, pSourceField, pMatchType, pIsDirect, customerKey, notePhone, "Notes", insertedNormalizedPhones, cancellationToken);
            }

            if (ticketNotesByCustomer.TryGetValue(customerKey, out List<string>? ticketNotes))
            {
                foreach (string ticketNote in ticketNotes)
                {
                    foreach (string notePhone in PhoneUtils.ExtractPhonesFromText(ticketNote))
                    {
                        count += await ProcessPhoneAsync(cmd, pCustKey, pPhoneNorm, pPhoneOrig, pSourceField, pMatchType, pIsDirect, customerKey, notePhone, "TicketNotes", insertedNormalizedPhones, cancellationToken);
                    }
                }
            }

            int done = i + 1;
            if (done % PhoneIndexProgressInterval == 0 || done == total)
            {
                UpdateProgress("phone_index", done, total, $"Rebuilding phone index... {done:N0} of {total:N0} customers", StagePhoneIndexStartPercent, StagePhoneIndexEndPercent);
            }
        }

        foreach (string normalizedPhone in insertedNormalizedPhones)
        {
            _cache.Remove(IdentityNegativeCachePrefix + normalizedPhone);
        }

        return count;
    }

    // Insert one CustomerPhones row for the given phone after normalizing to last 10 digits; skip if invalid.
    private static async Task<int> ProcessPhoneAsync(
        SqliteCommand cmd,
        SqliteParameter pCustKey,
        SqliteParameter pPhoneNorm,
        SqliteParameter pPhoneOrig,
        SqliteParameter pSourceField,
        SqliteParameter pMatchType,
        SqliteParameter pIsDirect,
        int customerKey,
        string? phone,
        string sourceField,
        HashSet<string> insertedNormalizedPhones,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return 0;

        string? norm = PhoneUtils.ExtractLast10Digits(phone);
        if (norm is null)
            return 0;

        (string matchType, int isDirect) = sourceField switch
        {
            "BusPhone" => ("direct_bus_phone", 1),
            "ResPhone" => ("direct_res_phone", 1),
            "TicketNotes" => ("ticket_note_reference", 0),
            _ => ("note_reference", 0)
        };

        pCustKey.Value = customerKey;
        pPhoneNorm.Value = norm;
        pPhoneOrig.Value = (object?)phone ?? DBNull.Value;
        pSourceField.Value = sourceField;
        pMatchType.Value = matchType;
        pIsDirect.Value = isDirect;
        int affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected > 0)
            _ = insertedNormalizedPhones.Add(norm);
        return affected;
    }

    // Helpers for counting rows in allowed tables (used by test endpoint).

    private async Task<Dictionary<string, int>> GetCountsFromDbAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        Dictionary<string, int> counts = new();
        foreach (string tableName in TableNames)
        {
            counts[tableName.ToLowerInvariant()] = await CountTableAsync(db, tableName, cancellationToken);
        }
        return counts;
    }

    private static async Task<int> CountTableAsync(AppDbContext db, string tableName, CancellationToken cancellationToken)
    {
        try
        {
            // Table names are hardcoded constants — no SQL injection risk.
#pragma warning disable EF1002
            return await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM " + tableName)
                .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
        }
        catch
        {
            return 0;
        }
    }

    private Dictionary<string, int> GetCountsFromDbSync(AppDbContext db)
    {
        Dictionary<string, int> counts = new();
        foreach (string tableName in TableNames)
        {
            counts[tableName.ToLowerInvariant()] = CountTableSync(db, tableName);
        }
        return counts;
    }

    private static int CountTableSync(AppDbContext db, string tableName)
    {
        try
        {
#pragma warning disable EF1002
            return db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM " + tableName)
                .FirstOrDefault();
#pragma warning restore EF1002
        }
        catch
        {
            return 0;
        }
    }

    // ── Transaction Helper ───────────────────────────────────────────

    private static async Task<T> ExecuteInTransactionAsync<T>(
        SqliteConnection conn,
        Func<SqliteTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction txn = conn.BeginTransaction();
        try
        {
            T result = await operation(txn, cancellationToken);
            await txn.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await txn.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ── Progress and Formatting ──────────────────────────────────────

    private void UpdateProgress(string stage, int current, int total, string message, int? percentMin = null, int? percentMax = null)
    {
        int percent;
        if (percentMin is int min && percentMax is int max && max > min)
        {
            percent = total > 0 ? min + (int)((double)current / total * (max - min)) : min;
        }
        else
        {
            percent = total > 0 ? (int)((double)current / total * 100) : 0;
        }
        if (percent > 100) percent = 100;

        string safeMessage = SanitizeProgressMessage(message);
        lock (_progressLock)
        {
            _currentProgress = new SyncProgress
            {
                InProgress = _syncInProgress,
                Stage = stage,
                Current = current,
                Total = total,
                Percent = percent,
                Message = safeMessage
            };
        }
    }

    private static string SanitizeProgressMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        const int maxLen = 500;
        string oneLine = message.Replace("\r", " ").Replace("\n", " ").Trim();
        if (oneLine.Length <= maxLen) return oneLine;
        return oneLine.Substring(0, maxLen) + "...";
    }

    private static Dictionary<string, object> BuildSyncStats(int customers, int tickets, int items, int payments, int phoneIndex, double duration)
    {
        return new Dictionary<string, object>
        {
            ["customers_synced"] = customers,
            ["tickets_synced"] = tickets,
            ["items_synced"] = items,
            ["payments_synced"] = payments,
            ["phone_index_count"] = phoneIndex,
            ["duration_seconds"] = duration
        };
    }

    private static string FormatCompletionMessage(double duration, int customers, int tickets, int items, int payments, int phoneIndex)
    {
        return $"Sync completed in {duration:F1}s. Customers: {customers:N0}, Tickets: {tickets:N0}, Items: {items:N0}, Payments: {payments:N0}, Phones: {phoneIndex:N0}";
    }
}
