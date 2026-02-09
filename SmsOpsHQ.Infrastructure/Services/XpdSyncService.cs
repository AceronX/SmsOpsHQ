using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// XPD to SQLite sync service. Streams data from XPawn MS Access database
// via cscript.exe / stream_table.vbs and batch-upserts into SQLite mirror tables.
public sealed class XpdSyncService : IXpdSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<XpdSyncService> _logger;

    private readonly string _xpdPath;
    private readonly string _mdwPath;
    private readonly string _xpdUser;
    private readonly string _xpdPassword;
    private readonly string _vbscriptPath;
    private readonly string _cscriptPath;

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private DateTime? _lastSync;
    private Dictionary<string, object> _lastSyncStats = new();
    private volatile bool _syncInProgress;

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
        _vbscriptPath = xpdSection["VbscriptPath"] ?? "stream_table.vbs";
        _cscriptPath = xpdSection["CscriptPath"] ?? @"C:\Windows\SysWOW64\cscript.exe";
    }

    public async Task<SyncResult> FullSyncAsync(CancellationToken cancellationToken = default)
    {
        if (_syncInProgress)
        {
            return new SyncResult { Success = false, Error = "Sync already in progress" };
        }

        bool lockAcquired = await _syncLock.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!lockAcquired)
        {
            return new SyncResult { Success = false, Error = "Sync already in progress" };
        }

        try
        {
            _syncInProgress = true;
            DateTime startTime = DateTime.UtcNow;
            UpdateProgress("init", 0, 5, "Initializing sync...");

            using IServiceScope scope = _scopeFactory.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Dictionary<string, int> sqliteBefore = await GetCountsFromDbAsync(db, cancellationToken);

            // Sync each table sequentially
            UpdateProgress("customers", 0, 100, "Syncing customers...");
            int customersSynced = await SyncCustomersAsync(db, cancellationToken);

            UpdateProgress("tickets", 0, 100, "Syncing tickets...");
            int ticketsSynced = await SyncTicketsAsync(db, cancellationToken);

            UpdateProgress("items", 0, 100, "Syncing items...");
            int itemsSynced = await SyncItemsAsync(db, cancellationToken);

            UpdateProgress("payments", 0, 100, "Syncing payments...");
            int paymentsSynced = await SyncPaymentsAsync(db, cancellationToken);

            UpdateProgress("phone_index", 0, 100, "Rebuilding phone index...");
            int phoneIndexCount = await RebuildPhoneIndexAsync(db, cancellationToken);

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

            UpdateProgress("complete", 100, 100, $"Sync completed in {durationSeconds:F1}s");

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
            UpdateProgress("error", 0, 100, $"Error: {ex.Message}");
            return new SyncResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _syncInProgress = false;
            _syncLock.Release();
        }
    }

    public SyncProgress GetProgress()
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

        return new SyncStatus
        {
            LastSync = _lastSync,
            SqliteCounts = counts,
            LastSyncStats = _lastSyncStats,
            SyncInProgress = _syncInProgress
        };
    }

    public async Task<Dictionary<string, int>> GetSqliteCountsAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await GetCountsFromDbAsync(db, cancellationToken);
    }

    // ── Sync Methods ──────────────────────────────────────────────────

    private async Task<int> SyncCustomersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;
        List<XpdCustomerEntity> batch = new(100);

        await foreach (JsonElement row in StreamVbscriptAsync("customers", cancellationToken))
        {
            XpdCustomerEntity entity = new()
            {
                Key = row.GetInt("key"),
                LastName = row.GetString("last_name"),
                FirstName = row.GetString("first_name"),
                MiddleName = row.GetString("middle_name"),
                Address = row.GetString("address"),
                City = row.GetString("city"),
                State = row.GetString("state"),
                Zip = row.GetString("zip"),
                ResPhone = row.GetString("res_phone"),
                BusPhone = row.GetString("bus_phone"),
                Email = row.GetString("email"),
                DOB = row.GetString("dob"),
                SSN = row.GetString("ssn"),
                IDNo = row.GetString("id_no"),
                IDIssueState = row.GetString("id_state"),
                Notes = row.GetString("notes"),
                FirstTransaction = row.GetString("first_transaction"),
                LastTransaction = row.GetString("last_transaction"),
                Warning = row.GetString("warning"),
                SyncedAt = now
            };

            batch.Add(entity);
            count++;

            if (batch.Count >= 100)
            {
                await UpsertCustomerBatchAsync(db, batch, cancellationToken);
                batch.Clear();
                UpdateProgress("customers", count, count + 500, $"Synced {count:N0} customers...");
            }
        }

        if (batch.Count > 0)
            await UpsertCustomerBatchAsync(db, batch, cancellationToken);

        return count;
    }

    private async Task<int> SyncTicketsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;
        List<XpdTicketEntity> batch = new(100);

        await foreach (JsonElement row in StreamVbscriptAsync("tickets", cancellationToken))
        {
            XpdTicketEntity entity = new()
            {
                Key = row.GetInt("key"),
                CustomerKey = row.GetInt("customer_key"),
                TransNo = row.GetNullableInt("trans_no"),
                Type = row.GetNullableInt("type"),
                Active = row.GetNullableInt("active"),
                Amount = row.GetNullableDouble("amount"),
                CurrentBalance = row.GetNullableDouble("current_balance"),
                IssueDate = row.GetString("issue_date"),
                DueDate = row.GetString("due_date"),
                DateClosed = row.GetString("date_closed"),
                HowClosed = row.GetString("how_closed"),
                Status = row.GetString("status"),
                Notes = row.GetString("notes"),
                Item = row.GetString("item"),
                OperatorInitials = row.GetString("operator_initials"),
                GunTicket = row.GetNullableInt("gun_ticket"),
                LostTicket = row.GetNullableInt("lost_ticket"),
                PaidTillDate = row.GetString("paid_till_date"),
                LastDate = row.GetString("last_date"),
                ChargesDue = row.GetNullableDouble("charges_due"),
                StandardCharges = row.GetNullableDouble("standard_charges"),
                StandardPU = row.GetNullableDouble("standard_pu"),
                FullTermPU = row.GetNullableDouble("fullterm_pu"),
                FulltermRenew = row.GetNullableDouble("fullterm_renew"),
                SyncedAt = now
            };

            batch.Add(entity);
            count++;

            if (batch.Count >= 100)
            {
                await UpsertTicketBatchAsync(db, batch, cancellationToken);
                batch.Clear();
                UpdateProgress("tickets", count, count + 500, $"Synced {count:N0} tickets...");
            }
        }

        if (batch.Count > 0)
            await UpsertTicketBatchAsync(db, batch, cancellationToken);

        return count;
    }

    private async Task<int> SyncItemsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;
        List<XpdItemEntity> batch = new(500);

        await foreach (JsonElement row in StreamVbscriptAsync("items", cancellationToken))
        {
            XpdItemEntity entity = new()
            {
                Key = row.GetInt("key"),
                TicketKey = row.GetInt("ticket_key"),
                PrintedDetail = row.GetString("printed_detail"),
                CategoryCode = row.GetString("category_code"),
                SerialNo = row.GetString("serial_no"),
                Cost = row.GetNullableDouble("cost"),
                ItemStatus = row.GetString("item_status"),
                Notes = row.GetString("notes"),
                Brand = row.GetString("brand"),
                Model = row.GetString("model"),
                Color = row.GetString("color"),
                Size = row.GetString("size"),
                Weight = row.GetString("weight"),
                Metal = row.GetString("metal"),
                SyncedAt = now
            };

            batch.Add(entity);
            count++;

            if (batch.Count >= 500)
            {
                await UpsertItemBatchAsync(db, batch, cancellationToken);
                batch.Clear();
                UpdateProgress("items", count, count + 1000, $"Synced {count:N0} items...");
            }
        }

        if (batch.Count > 0)
            await UpsertItemBatchAsync(db, batch, cancellationToken);

        return count;
    }

    private async Task<int> SyncPaymentsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;
        List<XpdPawnPaymentEntity> batch = new(500);

        await foreach (JsonElement row in StreamVbscriptAsync("payments", cancellationToken))
        {
            XpdPawnPaymentEntity entity = new()
            {
                Key = row.GetInt("key"),
                TicketKey = row.GetInt("ticket_key"),
                PaymentDate = row.GetString("payment_date"),
                PawnPmtType = row.GetNullableInt("pawn_pmt_type"),
                PaymentStatus = row.GetString("payment_status"),
                TotalDueAmount = row.GetNullableDouble("total_due_amount"),
                NetDueAmount = row.GetNullableDouble("net_due_amount"),
                NetPaymentAmount = row.GetNullableDouble("net_payment_amount"),
                Cash = row.GetNullableDouble("cash"),
                Check = row.GetNullableDouble("check"),
                CreditCard = row.GetNullableDouble("credit_card"),
                DebitCard = row.GetNullableDouble("debit_card"),
                InterestChargePaid = row.GetNullableDouble("interest_charge_paid"),
                ServiceChargePaid = row.GetNullableDouble("service_charge_paid"),
                PrincipalPaid = row.GetNullableDouble("principal_paid"),
                NewCurrentBalance = row.GetNullableDouble("new_current_balance"),
                NewDueDate = row.GetString("new_due_date"),
                OldDueDate = row.GetString("old_due_date"),
                OperatorInitials = row.GetString("operator_initials"),
                Method = row.GetString("method"),
                Note = row.GetString("note"),
                SyncedAt = now
            };

            batch.Add(entity);
            count++;

            if (batch.Count >= 500)
            {
                await UpsertPaymentBatchAsync(db, batch, cancellationToken);
                batch.Clear();
                UpdateProgress("payments", count, count + 1000, $"Synced {count:N0} payments...");
            }
        }

        if (batch.Count > 0)
            await UpsertPaymentBatchAsync(db, batch, cancellationToken);

        return count;
    }

    private async Task<int> RebuildPhoneIndexAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // Delete all existing phone entries
        await db.Database.ExecuteSqlRawAsync("DELETE FROM XPD_CustomerPhones", cancellationToken);

        // Load all customers to extract and normalize phones
        List<XpdCustomerEntity> customers = await db.XpdCustomers
            .AsNoTracking()
            .Select(c => new XpdCustomerEntity
            {
                Key = c.Key,
                ResPhone = c.ResPhone,
                BusPhone = c.BusPhone
            })
            .ToListAsync(cancellationToken);

        int total = customers.Count;
        int count = 0;
        List<XpdCustomerPhoneEntity> batch = new(1000);

        for (int i = 0; i < customers.Count; i++)
        {
            XpdCustomerEntity customer = customers[i];

            if (!string.IsNullOrWhiteSpace(customer.ResPhone))
            {
                string? normalized = PhoneUtils.ExtractLast10Digits(customer.ResPhone);
                if (normalized is not null)
                {
                    batch.Add(new XpdCustomerPhoneEntity
                    {
                        CustomerKey = customer.Key,
                        PhoneNormalized = normalized,
                        PhoneOriginal = customer.ResPhone,
                        PhoneType = "ResPhone"
                    });
                    count++;
                }
            }

            if (!string.IsNullOrWhiteSpace(customer.BusPhone)
                && customer.BusPhone != customer.ResPhone)
            {
                string? normalized = PhoneUtils.ExtractLast10Digits(customer.BusPhone);
                if (normalized is not null)
                {
                    batch.Add(new XpdCustomerPhoneEntity
                    {
                        CustomerKey = customer.Key,
                        PhoneNormalized = normalized,
                        PhoneOriginal = customer.BusPhone,
                        PhoneType = "BusPhone"
                    });
                    count++;
                }
            }

            if (batch.Count >= 1000)
            {
                await InsertPhoneBatchAsync(db, batch, cancellationToken);
                batch.Clear();
                UpdateProgress("phone_index", i + 1, total, $"Indexed {i + 1:N0}/{total:N0} customers...");
            }
        }

        if (batch.Count > 0)
            await InsertPhoneBatchAsync(db, batch, cancellationToken);

        return count;
    }

    // ── Batch Upsert Helpers (Raw SQL INSERT OR REPLACE) ──────────────
    // Uses the IEnumerable<object> overload to properly separate SQL parameters
    // from the CancellationToken. Nullable values are boxed via Param() helper.

    private static object Param(object? value) => value ?? DBNull.Value;

    private static async Task UpsertCustomerBatchAsync(
        AppDbContext db, List<XpdCustomerEntity> batch, CancellationToken ct)
    {
        const string sql = @"INSERT OR REPLACE INTO XPD_Customers
                  (Key, LastName, FirstName, MiddleName, Address, City, State, Zip,
                   ResPhone, BusPhone, Email, DOB, SSN, IDNo, IDIssueState,
                   Notes, FirstTransaction, LastTransaction, Warning, SyncedAt)
                  VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19})";

        foreach (XpdCustomerEntity e in batch)
        {
            object[] parameters =
            [
                e.Key, Param(e.LastName), Param(e.FirstName), Param(e.MiddleName),
                Param(e.Address), Param(e.City), Param(e.State), Param(e.Zip),
                Param(e.ResPhone), Param(e.BusPhone), Param(e.Email),
                Param(e.DOB), Param(e.SSN), Param(e.IDNo), Param(e.IDIssueState),
                Param(e.Notes), Param(e.FirstTransaction), Param(e.LastTransaction),
                Param(e.Warning), Param(e.SyncedAt)
            ];
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    private static async Task UpsertTicketBatchAsync(
        AppDbContext db, List<XpdTicketEntity> batch, CancellationToken ct)
    {
        const string sql = @"INSERT OR REPLACE INTO XPD_Tickets
                  (Key, CustomerKey, TransNo, Type, Active, Amount, CurrentBalance,
                   IssueDate, DueDate, DateClosed, HowClosed, Status, Notes, Item,
                   OperatorInitials, GunTicket, LostTicket, PaidTillDate, LastDate,
                   ChargesDue, StandardCharges, StandardPU, FullTermPU, FulltermRenew, SyncedAt)
                  VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24})";

        foreach (XpdTicketEntity e in batch)
        {
            object[] parameters =
            [
                e.Key, e.CustomerKey, Param(e.TransNo), Param(e.Type), Param(e.Active),
                Param(e.Amount), Param(e.CurrentBalance),
                Param(e.IssueDate), Param(e.DueDate), Param(e.DateClosed), Param(e.HowClosed),
                Param(e.Status), Param(e.Notes), Param(e.Item),
                Param(e.OperatorInitials), Param(e.GunTicket), Param(e.LostTicket),
                Param(e.PaidTillDate), Param(e.LastDate),
                Param(e.ChargesDue), Param(e.StandardCharges), Param(e.StandardPU),
                Param(e.FullTermPU), Param(e.FulltermRenew), Param(e.SyncedAt)
            ];
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    private static async Task UpsertItemBatchAsync(
        AppDbContext db, List<XpdItemEntity> batch, CancellationToken ct)
    {
        const string sql = @"INSERT OR REPLACE INTO XPD_Items
                  (Key, TicketKey, PrintedDetail, CategoryCode, SerialNo, Cost,
                   ItemStatus, Notes, Brand, Model, Color, Size, Weight, Metal, SyncedAt)
                  VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14})";

        foreach (XpdItemEntity e in batch)
        {
            object[] parameters =
            [
                e.Key, e.TicketKey, Param(e.PrintedDetail), Param(e.CategoryCode),
                Param(e.SerialNo), Param(e.Cost),
                Param(e.ItemStatus), Param(e.Notes), Param(e.Brand), Param(e.Model),
                Param(e.Color), Param(e.Size), Param(e.Weight), Param(e.Metal), Param(e.SyncedAt)
            ];
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    private static async Task UpsertPaymentBatchAsync(
        AppDbContext db, List<XpdPawnPaymentEntity> batch, CancellationToken ct)
    {
        const string sql = @"INSERT OR REPLACE INTO XPD_PawnPayments
                  (Key, TicketKey, PaymentDate, PawnPmtType, PaymentStatus,
                   TotalDueAmount, NetDueAmount, NetPaymentAmount, Cash, Check_,
                   CreditCard, DebitCard, InterestChargePaid, ServiceChargePaid,
                   PrincipalPaid, NewCurrentBalance, NewDueDate, OldDueDate,
                   OperatorInitials, Method, Note, SyncedAt)
                  VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21})";

        foreach (XpdPawnPaymentEntity e in batch)
        {
            object[] parameters =
            [
                e.Key, e.TicketKey, Param(e.PaymentDate), Param(e.PawnPmtType),
                Param(e.PaymentStatus),
                Param(e.TotalDueAmount), Param(e.NetDueAmount), Param(e.NetPaymentAmount),
                Param(e.Cash), Param(e.Check),
                Param(e.CreditCard), Param(e.DebitCard), Param(e.InterestChargePaid),
                Param(e.ServiceChargePaid),
                Param(e.PrincipalPaid), Param(e.NewCurrentBalance), Param(e.NewDueDate),
                Param(e.OldDueDate),
                Param(e.OperatorInitials), Param(e.Method), Param(e.Note), Param(e.SyncedAt)
            ];
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    private static async Task InsertPhoneBatchAsync(
        AppDbContext db, List<XpdCustomerPhoneEntity> batch, CancellationToken ct)
    {
        const string sql = @"INSERT OR IGNORE INTO XPD_CustomerPhones
                  (CustomerKey, PhoneNormalized, PhoneOriginal, PhoneType)
                  VALUES ({0},{1},{2},{3})";

        foreach (XpdCustomerPhoneEntity e in batch)
        {
            object[] parameters =
            [
                e.CustomerKey, e.PhoneNormalized,
                Param(e.PhoneOriginal), e.PhoneType
            ];
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    // ── VBScript Streaming ────────────────────────────────────────────

    private async IAsyncEnumerable<JsonElement> StreamVbscriptAsync(
        string queryType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _cscriptPath,
            Arguments = $"//nologo \"{_vbscriptPath}\" \"{_xpdPath}\" \"{queryType}\" \"{_mdwPath}\" \"{_xpdUser}\" \"{_xpdPassword}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start VBScript process for {QueryType}", queryType);
            yield break;
        }

        using StreamReader stdoutReader = process.StandardOutput;

        while (!stdoutReader.EndOfStream)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(); } catch { /* best effort */ }
                yield break;
            }

            string? line = await stdoutReader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("{\"error\""))
            {
                _logger.LogWarning("VBScript error: {Error}", line);
                continue;
            }

            JsonElement element;
            try
            {
                element = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parse error on line: {Line}", line.Length > 100 ? line[..100] : line);
                continue;
            }

            yield return element;
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            _logger.LogWarning("VBScript exited with code {ExitCode}: {Stderr}", process.ExitCode, stderr);
        }
    }

    // ── Count Helpers ─────────────────────────────────────────────────

    private static async Task<Dictionary<string, int>> GetCountsFromDbAsync(
        AppDbContext db, CancellationToken ct)
    {
        Dictionary<string, int> counts = new();

        counts["customers"] = await CountTableAsync(db, "XPD_Customers", ct);
        counts["tickets"] = await CountTableAsync(db, "XPD_Tickets", ct);
        counts["items"] = await CountTableAsync(db, "XPD_Items", ct);
        counts["pawnpayments"] = await CountTableAsync(db, "XPD_PawnPayments", ct);
        counts["customerphones"] = await CountTableAsync(db, "XPD_CustomerPhones", ct);

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

        counts["customers"] = CountTableSync(db, "XPD_Customers");
        counts["tickets"] = CountTableSync(db, "XPD_Tickets");
        counts["items"] = CountTableSync(db, "XPD_Items");
        counts["pawnpayments"] = CountTableSync(db, "XPD_PawnPayments");
        counts["customerphones"] = CountTableSync(db, "XPD_CustomerPhones");

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

    private void UpdateProgress(string stage, int current, int total, string message)
    {
        int percent = total > 0 ? (int)((double)current / total * 100) : 0;
        _currentProgress = new SyncProgress
        {
            InProgress = _syncInProgress,
            Stage = stage,
            Current = current,
            Total = total,
            Percent = percent,
            Message = message
        };
    }
}

// Extension methods for reading JSON properties from VBScript output.
internal static class JsonElementExtensions
{
    public static int GetInt(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => 0
        };
    }

    public static int? GetNullableInt(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null
        };
    }

    public static double? GetNullableDouble(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed) => parsed,
            _ => null
        };
    }

    public static string? GetString(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => value.ToString()
        };
    }
}
