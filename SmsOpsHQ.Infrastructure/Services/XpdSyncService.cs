using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    private readonly object _progressLock = new();
    private SyncProgress _currentProgress = new()
    {
        Stage = string.Empty,
        Message = string.Empty
    };

    public XpdSyncService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<XpdSyncService> logger)
    {
        _scopeFactory = scopeFactory;
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
            UpdateProgress("init", 0, 5, "Initializing sync...", 0, 2);

            using IServiceScope scope = _scopeFactory.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Dictionary<string, int> sqliteBefore = await GetCountsFromDbAsync(db, cancellationToken);

            // Get the raw SQLite connection for fast bulk operations
            DbConnection dbConnection = db.Database.GetDbConnection();
            if (dbConnection.State != ConnectionState.Open)
                await dbConnection.OpenAsync(cancellationToken);
            SqliteConnection conn = (SqliteConnection)dbConnection;

            // SQLite performance PRAGMAs
            using (SqliteCommand pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!File.Exists(_exportScriptPath))
                throw new InvalidOperationException($"Export script not found: {_exportScriptPath}. Ensure export_xpd_to_sql.vbs is deployed.");

            (int customersSynced, int ticketsSynced, int itemsSynced, int paymentsSynced) = await RunSqlExportSyncAsync(conn, cancellationToken);
            UpdateProgress("phone_index", 0, 1, "Rebuilding phone index...", 80, 80);
            int phoneIndexCount = await RebuildPhoneIndexAsync(conn, db, cancellationToken);

            Dictionary<string, int> sqliteAfter = await GetCountsFromDbAsync(db, cancellationToken);

            DateTime endTime = DateTime.UtcNow;
            double durationSeconds = (endTime - startTime).TotalSeconds;

            _lastSync = endTime;
            _lastSyncStats = new Dictionary<string, object>
            {
                ["customers_synced"] = customersSynced,
                ["tickets_synced"] = ticketsSynced,
                ["items_synced"] = itemsSynced,
                ["payments_synced"] = paymentsSynced,
                ["phone_index_count"] = phoneIndexCount,
                ["duration_seconds"] = durationSeconds
            };

            _lastError = null;
            string completeMsg = $"Sync completed in {durationSeconds:F1}s. Customers: {customersSynced:N0}, Tickets: {ticketsSynced:N0}, Items: {itemsSynced:N0}, Payments: {paymentsSynced:N0}, Phones: {phoneIndexCount:N0}";
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

    // ── SQL export sync (one script, one file, one transaction) ──
    private async Task<(int Customers, int Tickets, int Items, int Payments)> RunSqlExportSyncAsync(
        SqliteConnection conn, CancellationToken ct)
    {
        string xpdPath = _currentRunOverrides?.XpdPath ?? _xpdPath;
        string mdwPath = _currentRunOverrides?.MdwPath ?? _mdwPath;
        string xpdUser = _currentRunOverrides?.XpdUser ?? _xpdUser;
        string xpdPassword = _currentRunOverrides?.XpdPassword ?? _xpdPassword;

        string tempFile = Path.Combine(Path.GetTempPath(), "smsops_sync_" + Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            UpdateProgress("export", 0, 100, "Exporting from Access...", 0, 10);
            ProcessStartInfo startInfo = new()
            {
                FileName = _cscriptPath,
                Arguments = $"//nologo \"{_exportScriptPath}\" \"{xpdPath}\" \"{mdwPath}\" \"{xpdUser}\" \"{xpdPassword}\" \"{tempFile}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process process = new() { StartInfo = startInfo };
            process.Start();
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogInformation("VBScript output: {Stderr}", stderr.Trim());
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Export script failed." : stderr.Trim());
            }

            if (!File.Exists(tempFile))
                throw new InvalidOperationException("Export script did not produce SQL file.");

            UpdateProgress("import", 0, 1, "Reading SQL file...", 10, 10);
            string[] lines = await File.ReadAllLinesAsync(tempFile, ct);
            int totalLines = lines.Length;
            int customers = 0, tickets = 0, items = 0, payments = 0;
            const int progressInterval = 500;

            using SqliteTransaction txn = conn.BeginTransaction();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            // Recreate PawnPayments with columns matching the actual XPD schema (verified by database inspection).
            // "Check" is a reserved word in SQLite, so stored as "Check_".
            cmd.CommandText = "DROP TABLE IF EXISTS PawnPayments;";
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.CommandText = @"CREATE TABLE PawnPayments (
                Key INTEGER PRIMARY KEY,
                TicketKey INTEGER,
                PaymentDate TEXT,
                PawnPmtType INTEGER,
                PaymentStatus TEXT,
                TotalDueAmount REAL,
                NetDueAmount REAL,
                NetPaymentAmount REAL,
                Cash REAL,
                Check_ REAL,
                CreditCard REAL,
                DebitCard REAL,
                InterestChargePaid REAL,
                ServiceChargePaid REAL,
                PrincipalPaid REAL,
                NewCurrentBalance REAL,
                NewDueDate TEXT,
                OldDueDate TEXT,
                OperatorInitials TEXT,
                Method TEXT,
                Note TEXT,
                SyncedAt TEXT
            );";
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_PawnPayments_TicketKey ON PawnPayments (TicketKey);";
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_PawnPayments_PaymentDate ON PawnPayments (PaymentDate);";
            await cmd.ExecuteNonQueryAsync(ct);
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (string.IsNullOrEmpty(s) || s.Equals("BEGIN TRANSACTION;", StringComparison.OrdinalIgnoreCase) || s.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (s.StartsWith("INSERT INTO Customers", StringComparison.OrdinalIgnoreCase)) customers++;
                else if (s.StartsWith("INSERT OR REPLACE INTO Tickets", StringComparison.OrdinalIgnoreCase)) tickets++;
                else if (s.StartsWith("INSERT OR REPLACE INTO Items", StringComparison.OrdinalIgnoreCase)) items++;
                else if (s.StartsWith("INSERT OR REPLACE INTO PawnPayments", StringComparison.OrdinalIgnoreCase)) payments++;
                cmd.CommandText = s;
                await cmd.ExecuteNonQueryAsync(ct);

                int processed = i + 1;
                if (processed % progressInterval == 0 || processed == totalLines)
                {
                    string msg = $"Importing... {processed:N0} of {totalLines:N0} rows (Customers: {customers:N0}, Tickets: {tickets:N0}, Items: {items:N0}, Payments: {payments:N0})";
                    UpdateProgress("import", processed, totalLines, msg, 10, 80);
                }
            }
            await txn.CommitAsync(ct);

            if (payments == 0 && (customers > 0 || tickets > 0))
                _logger.LogWarning("PawnPayments sync produced 0 rows while Customers={Customers}, Tickets={Tickets}, Items={Items}. Check VBScript stderr above for errors.", customers, tickets, items);

            return (customers, tickets, items, payments);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    private async Task<int> RebuildPhoneIndexAsync(SqliteConnection conn, AppDbContext db, CancellationToken ct)
    {
        List<CustomerEntity> customers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerKey != null)
            .Select(c => new CustomerEntity { CustomerKey = c.CustomerKey, ResPhone = c.ResPhone, BusPhone = c.BusPhone })
            .ToListAsync(ct);

        int total = customers.Count;
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();

        using (SqliteCommand delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = txn;
            delCmd.CommandText = "DELETE FROM CustomerPhones";
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT OR IGNORE INTO CustomerPhones (CustomerKey, PhoneNormalized, PhoneOriginal, PhoneType) VALUES ($custKey,$phoneNorm,$phoneOrig,$phoneType)";
        SqliteParameter pCustKey = cmd.Parameters.Add("$custKey", SqliteType.Integer);
        SqliteParameter pPhoneNorm = cmd.Parameters.Add("$phoneNorm", SqliteType.Text);
        SqliteParameter pPhoneOrig = cmd.Parameters.Add("$phoneOrig", SqliteType.Text);
        SqliteParameter pPhoneType = cmd.Parameters.Add("$phoneType", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < customers.Count; i++)
        {
            CustomerEntity c = customers[i];
            int customerKey = c.CustomerKey!.Value;

            if (!string.IsNullOrWhiteSpace(c.ResPhone))
            {
                string? norm = PhoneUtils.ExtractLast10Digits(c.ResPhone);
                if (norm is not null)
                {
                    pCustKey.Value = customerKey;
                    pPhoneNorm.Value = norm;
                    pPhoneOrig.Value = (object?)c.ResPhone ?? DBNull.Value;
                    pPhoneType.Value = "ResPhone";
                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
            }

            if (!string.IsNullOrWhiteSpace(c.BusPhone) && c.BusPhone != c.ResPhone)
            {
                string? norm = PhoneUtils.ExtractLast10Digits(c.BusPhone);
                if (norm is not null)
                {
                    pCustKey.Value = customerKey;
                    pPhoneNorm.Value = norm;
                    pPhoneOrig.Value = (object?)c.BusPhone ?? DBNull.Value;
                    pPhoneType.Value = "BusPhone";
                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
            }

            int done = i + 1;
            if (done % 1000 == 0 || done == total)
                UpdateProgress("phone_index", done, total, $"Rebuilding phone index... {done:N0} of {total:N0} customers", 80, 100);
        }

        await txn.CommitAsync(ct);
        return count;
    }

    // ── Count Helpers ─────────────────────────────────────────────────

    private static async Task<Dictionary<string, int>> GetCountsFromDbAsync(
        AppDbContext db, CancellationToken ct)
    {
        Dictionary<string, int> counts = new();

        counts["customers"] = await CountTableAsync(db, "Customers", ct);
        counts["tickets"] = await CountTableAsync(db, "Tickets", ct);
        counts["items"] = await CountTableAsync(db, "Items", ct);
        counts["pawnpayments"] = await CountTableAsync(db, "PawnPayments", ct);
        counts["customerphones"] = await CountTableAsync(db, "CustomerPhones", ct);

        return counts;
    }

    private static async Task<int> CountTableAsync(AppDbContext db, string tableName, CancellationToken ct)
    {
        try
        {
            // Table names are hardcoded constants — no SQL injection risk.
#pragma warning disable EF1002
            return await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM " + tableName)
                .FirstOrDefaultAsync(ct);
#pragma warning restore EF1002
        }
        catch
        {
            return 0;
        }
    }

    private static Dictionary<string, int> GetCountsFromDbSync(AppDbContext db)
    {
        Dictionary<string, int> counts = new();

        counts["customers"] = CountTableSync(db, "Customers");
        counts["tickets"] = CountTableSync(db, "Tickets");
        counts["items"] = CountTableSync(db, "Items");
        counts["pawnpayments"] = CountTableSync(db, "PawnPayments");
        counts["customerphones"] = CountTableSync(db, "CustomerPhones");

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

    /// <summary>Sanitize progress message for JSON/UI: single line, reasonable length.</summary>
    private static string SanitizeProgressMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        const int maxLen = 500;
        string oneLine = message.Replace("\r", " ").Replace("\n", " ").Trim();
        if (oneLine.Length <= maxLen) return oneLine;
        return oneLine.Substring(0, maxLen) + "...";
    }
}
