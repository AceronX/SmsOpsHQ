using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
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

// XPD to SQLite sync service. Streams data from XPawn MS Access database
// via cscript.exe / stream_table.vbs and batch-upserts into SQLite tables
// (Customers, Tickets, Items, PawnPayments, CustomerPhones).
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

    private volatile SyncRunOptions? _currentRunOverrides;

    private DateTime? _lastSync;
    private Dictionary<string, object> _lastSyncStats = new();
    private volatile bool _syncInProgress;
    private string? _lastError;

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
        string vbscriptRaw = xpdSection["VbscriptPath"] ?? "stream_table.vbs";
        _vbscriptPath = Path.IsPathRooted(vbscriptRaw)
            ? vbscriptRaw
            : Path.Combine(AppContext.BaseDirectory, vbscriptRaw);
        _cscriptPath = xpdSection["CscriptPath"] ?? @"C:\Windows\SysWOW64\cscript.exe";
    }

    public async Task<SyncResult> FullSyncAsync(SyncRunOptions? overrides = null, CancellationToken cancellationToken = default)
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

        _currentRunOverrides = overrides;
        _lastError = null;
        try
        {
            _syncInProgress = true;
            DateTime startTime = DateTime.UtcNow;
            UpdateProgress("init", 0, 5, "Initializing sync...");

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

            // Sync each table sequentially inside transactions
            UpdateProgress("customers", 0, 100, "Syncing customers...");
            int customersSynced = await SyncCustomersAsync(conn, cancellationToken);

            UpdateProgress("tickets", 0, 100, "Syncing tickets...");
            int ticketsSynced = await SyncTicketsAsync(conn, cancellationToken);

            UpdateProgress("items", 0, 100, "Syncing items...");
            int itemsSynced = await SyncItemsAsync(conn, cancellationToken);

            UpdateProgress("payments", 0, 100, "Syncing payments...");
            int paymentsSynced = await SyncPaymentsAsync(conn, cancellationToken);

            UpdateProgress("phone_index", 0, 100, "Rebuilding phone index...");
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
            _lastError = ex.Message;
            UpdateProgress("error", 0, 100, $"Error: {ex.Message}");
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

    // ── Sync Methods (Raw SqliteCommand + Transactions for speed) ─────
    // Each table sync: BEGIN TRANSACTION → prepared INSERT → COMMIT.
    // This gives ~50,000+ rows/sec vs ~60 rows/sec without transactions.

    private async Task<int> SyncCustomersAsync(SqliteConnection conn, CancellationToken ct)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT INTO Customers
            (CustomerKey, LastName, FirstName, MiddleName,
             Address, City, State, Zip, ResPhone, BusPhone, EMailAddress,
             DOB, SSN, IDNo, IDIssueState, Notes,
             FirstTransaction, LastTransaction, Warning, SyncedAt,
             PhoneE164, StoreId, CreatedAt, UpdatedAt)
            VALUES ($key,$lastName,$firstName,$middleName,
             $address,$city,$state,$zip,$resPhone,$busPhone,$email,
             $dob,$ssn,$idNo,$idIssueState,$notes,
             $firstTxn,$lastTxn,$warning,$syncedAt,
             $phoneE164,$storeId,$createdAt,$updatedAt)
            ON CONFLICT(CustomerKey) DO UPDATE SET
             LastName=excluded.LastName, FirstName=excluded.FirstName,
             MiddleName=excluded.MiddleName, Address=excluded.Address,
             City=excluded.City, State=excluded.State, Zip=excluded.Zip,
             ResPhone=excluded.ResPhone, BusPhone=excluded.BusPhone,
             EMailAddress=excluded.EMailAddress, DOB=excluded.DOB,
             SSN=excluded.SSN, IDNo=excluded.IDNo,
             IDIssueState=excluded.IDIssueState, Notes=excluded.Notes,
             FirstTransaction=excluded.FirstTransaction,
             LastTransaction=excluded.LastTransaction,
             Warning=excluded.Warning, SyncedAt=excluded.SyncedAt,
             UpdatedAt=excluded.UpdatedAt";

        // Pre-add all parameters once (prepared statement pattern)
        SqliteParameter pKey = cmd.Parameters.Add("$key", SqliteType.Integer);
        SqliteParameter pLastName = cmd.Parameters.Add("$lastName", SqliteType.Text);
        SqliteParameter pFirstName = cmd.Parameters.Add("$firstName", SqliteType.Text);
        SqliteParameter pMiddleName = cmd.Parameters.Add("$middleName", SqliteType.Text);
        SqliteParameter pAddress = cmd.Parameters.Add("$address", SqliteType.Text);
        SqliteParameter pCity = cmd.Parameters.Add("$city", SqliteType.Text);
        SqliteParameter pState = cmd.Parameters.Add("$state", SqliteType.Text);
        SqliteParameter pZip = cmd.Parameters.Add("$zip", SqliteType.Text);
        SqliteParameter pResPhone = cmd.Parameters.Add("$resPhone", SqliteType.Text);
        SqliteParameter pBusPhone = cmd.Parameters.Add("$busPhone", SqliteType.Text);
        SqliteParameter pEmail = cmd.Parameters.Add("$email", SqliteType.Text);
        SqliteParameter pDob = cmd.Parameters.Add("$dob", SqliteType.Text);
        SqliteParameter pSsn = cmd.Parameters.Add("$ssn", SqliteType.Text);
        SqliteParameter pIdNo = cmd.Parameters.Add("$idNo", SqliteType.Text);
        SqliteParameter pIdIssueState = cmd.Parameters.Add("$idIssueState", SqliteType.Text);
        SqliteParameter pNotes = cmd.Parameters.Add("$notes", SqliteType.Text);
        SqliteParameter pFirstTxn = cmd.Parameters.Add("$firstTxn", SqliteType.Text);
        SqliteParameter pLastTxn = cmd.Parameters.Add("$lastTxn", SqliteType.Text);
        SqliteParameter pWarning = cmd.Parameters.Add("$warning", SqliteType.Text);
        SqliteParameter pSyncedAt = cmd.Parameters.Add("$syncedAt", SqliteType.Text);
        SqliteParameter pPhoneE164 = cmd.Parameters.Add("$phoneE164", SqliteType.Text);
        SqliteParameter pStoreId = cmd.Parameters.Add("$storeId", SqliteType.Integer);
        SqliteParameter pCreatedAt = cmd.Parameters.Add("$createdAt", SqliteType.Text);
        SqliteParameter pUpdatedAt = cmd.Parameters.Add("$updatedAt", SqliteType.Text);
        cmd.Prepare();

        await foreach (JsonElement row in StreamVbscriptAsync("customers", ct))
        {
            pKey.Value = row.GetInt("key");
            pLastName.Value = (object?)row.GetString("last_name") ?? DBNull.Value;
            pFirstName.Value = (object?)row.GetString("first_name") ?? DBNull.Value;
            pMiddleName.Value = (object?)row.GetString("middle_name") ?? DBNull.Value;
            pAddress.Value = (object?)row.GetString("address") ?? DBNull.Value;
            pCity.Value = (object?)row.GetString("city") ?? DBNull.Value;
            pState.Value = (object?)row.GetString("state") ?? DBNull.Value;
            pZip.Value = (object?)row.GetString("zip") ?? DBNull.Value;
            pResPhone.Value = (object?)row.GetString("res_phone") ?? DBNull.Value;
            pBusPhone.Value = (object?)row.GetString("bus_phone") ?? DBNull.Value;
            pEmail.Value = (object?)row.GetString("email_address") ?? DBNull.Value;
            pDob.Value = (object?)row.GetString("dob") ?? DBNull.Value;
            pSsn.Value = (object?)row.GetString("ssn") ?? DBNull.Value;
            pIdNo.Value = (object?)row.GetString("id_no") ?? DBNull.Value;
            pIdIssueState.Value = (object?)row.GetString("id_issue_state") ?? DBNull.Value;
            pNotes.Value = (object?)row.GetString("notes") ?? DBNull.Value;
            pFirstTxn.Value = (object?)row.GetString("first_transaction") ?? DBNull.Value;
            pLastTxn.Value = (object?)row.GetString("last_transaction") ?? DBNull.Value;
            pWarning.Value = (object?)row.GetString("warning") ?? DBNull.Value;
            pSyncedAt.Value = now;
            pPhoneE164.Value = "";
            pStoreId.Value = 1;
            pCreatedAt.Value = now;
            pUpdatedAt.Value = now;

            await cmd.ExecuteNonQueryAsync(ct);
            count++;

            if (count % 500 == 0)
                UpdateProgress("customers", count, count + 500, $"Synced {count:N0} customers...");
        }

        await txn.CommitAsync(ct);
        return count;
    }

    private async Task<int> SyncTicketsAsync(SqliteConnection conn, CancellationToken ct)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT OR REPLACE INTO Tickets
            (Key, CustomerKey, TransNo, Type, Active, Amount, CurrentBalance,
             IssueDate, DueDate, DateClosed, HowClosed, Status, Notes, Item,
             OperatorInitials, GunTicket, LostTicket, PaidTillDate, LastDate,
             ChargesDue, StandardCharges, StandardPU, FullTermPU, FulltermRenew, SyncedAt)
            VALUES ($key,$custKey,$transNo,$type,$active,$amount,$curBal,
             $issueDate,$dueDate,$dateClosed,$howClosed,$status,$notes,$item,
             $opInit,$gunTicket,$lostTicket,$paidTill,$lastDate,
             $chargesDue,$stdCharges,$stdPU,$ftPU,$ftRenew,$syncedAt)";

        SqliteParameter pKey = cmd.Parameters.Add("$key", SqliteType.Integer);
        SqliteParameter pCustKey = cmd.Parameters.Add("$custKey", SqliteType.Integer);
        SqliteParameter pTransNo = cmd.Parameters.Add("$transNo", SqliteType.Integer);
        SqliteParameter pType = cmd.Parameters.Add("$type", SqliteType.Integer);
        SqliteParameter pActive = cmd.Parameters.Add("$active", SqliteType.Integer);
        SqliteParameter pAmount = cmd.Parameters.Add("$amount", SqliteType.Real);
        SqliteParameter pCurBal = cmd.Parameters.Add("$curBal", SqliteType.Real);
        SqliteParameter pIssueDate = cmd.Parameters.Add("$issueDate", SqliteType.Text);
        SqliteParameter pDueDate = cmd.Parameters.Add("$dueDate", SqliteType.Text);
        SqliteParameter pDateClosed = cmd.Parameters.Add("$dateClosed", SqliteType.Text);
        SqliteParameter pHowClosed = cmd.Parameters.Add("$howClosed", SqliteType.Text);
        SqliteParameter pStatus = cmd.Parameters.Add("$status", SqliteType.Text);
        SqliteParameter pNotes = cmd.Parameters.Add("$notes", SqliteType.Text);
        SqliteParameter pItem = cmd.Parameters.Add("$item", SqliteType.Text);
        SqliteParameter pOpInit = cmd.Parameters.Add("$opInit", SqliteType.Text);
        SqliteParameter pGunTicket = cmd.Parameters.Add("$gunTicket", SqliteType.Integer);
        SqliteParameter pLostTicket = cmd.Parameters.Add("$lostTicket", SqliteType.Integer);
        SqliteParameter pPaidTill = cmd.Parameters.Add("$paidTill", SqliteType.Text);
        SqliteParameter pLastDate = cmd.Parameters.Add("$lastDate", SqliteType.Text);
        SqliteParameter pChargesDue = cmd.Parameters.Add("$chargesDue", SqliteType.Real);
        SqliteParameter pStdCharges = cmd.Parameters.Add("$stdCharges", SqliteType.Real);
        SqliteParameter pStdPU = cmd.Parameters.Add("$stdPU", SqliteType.Real);
        SqliteParameter pFtPU = cmd.Parameters.Add("$ftPU", SqliteType.Real);
        SqliteParameter pFtRenew = cmd.Parameters.Add("$ftRenew", SqliteType.Real);
        SqliteParameter pSyncedAt = cmd.Parameters.Add("$syncedAt", SqliteType.Text);
        cmd.Prepare();

        await foreach (JsonElement row in StreamVbscriptAsync("tickets", ct))
        {
            pKey.Value = row.GetInt("key");
            pCustKey.Value = row.GetInt("customer_key");
            pTransNo.Value = NullableVal(row.GetNullableInt("trans_no"));
            pType.Value = NullableVal(row.GetNullableInt("type"));
            pActive.Value = NullableVal(row.GetNullableInt("active"));
            pAmount.Value = NullableVal(row.GetNullableDouble("amount"));
            pCurBal.Value = NullableVal(row.GetNullableDouble("current_balance"));
            pIssueDate.Value = NullableVal(row.GetString("issue_date"));
            pDueDate.Value = NullableVal(row.GetString("due_date"));
            pDateClosed.Value = NullableVal(row.GetString("date_closed"));
            pHowClosed.Value = NullableVal(row.GetString("how_closed"));
            pStatus.Value = NullableVal(row.GetString("status"));
            pNotes.Value = NullableVal(row.GetString("notes"));
            pItem.Value = NullableVal(row.GetString("item"));
            pOpInit.Value = NullableVal(row.GetString("operator_initials"));
            pGunTicket.Value = NullableVal(row.GetNullableInt("gun_ticket"));
            pLostTicket.Value = NullableVal(row.GetNullableInt("lost_ticket"));
            pPaidTill.Value = NullableVal(row.GetString("paid_till_date"));
            pLastDate.Value = NullableVal(row.GetString("last_date"));
            pChargesDue.Value = NullableVal(row.GetNullableDouble("charges_due"));
            pStdCharges.Value = NullableVal(row.GetNullableDouble("standard_charges"));
            pStdPU.Value = NullableVal(row.GetNullableDouble("standard_pu"));
            pFtPU.Value = NullableVal(row.GetNullableDouble("fullterm_pu"));
            pFtRenew.Value = NullableVal(row.GetNullableDouble("fullterm_renew"));
            pSyncedAt.Value = now;

            await cmd.ExecuteNonQueryAsync(ct);
            count++;

            if (count % 500 == 0)
                UpdateProgress("tickets", count, count + 500, $"Synced {count:N0} tickets...");
        }

        await txn.CommitAsync(ct);
        return count;
    }

    private async Task<int> SyncItemsAsync(SqliteConnection conn, CancellationToken ct)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT OR REPLACE INTO Items
            (Key, TicketKey, PrintedDetail, CategoryCode, SerialNo, Cost,
             ItemStatus, Notes, Mfg, Model, Color, Size, Weight, Karat, SyncedAt)
            VALUES ($key,$ticketKey,$printed,$catCode,$serial,$cost,
             $itemStatus,$notes,$mfg,$model,$color,$size,$weight,$karat,$syncedAt)";

        SqliteParameter pKey = cmd.Parameters.Add("$key", SqliteType.Integer);
        SqliteParameter pTicketKey = cmd.Parameters.Add("$ticketKey", SqliteType.Integer);
        SqliteParameter pPrinted = cmd.Parameters.Add("$printed", SqliteType.Text);
        SqliteParameter pCatCode = cmd.Parameters.Add("$catCode", SqliteType.Text);
        SqliteParameter pSerial = cmd.Parameters.Add("$serial", SqliteType.Text);
        SqliteParameter pCost = cmd.Parameters.Add("$cost", SqliteType.Real);
        SqliteParameter pItemStatus = cmd.Parameters.Add("$itemStatus", SqliteType.Text);
        SqliteParameter pNotes = cmd.Parameters.Add("$notes", SqliteType.Text);
        SqliteParameter pMfg = cmd.Parameters.Add("$mfg", SqliteType.Text);
        SqliteParameter pModel = cmd.Parameters.Add("$model", SqliteType.Text);
        SqliteParameter pColor = cmd.Parameters.Add("$color", SqliteType.Text);
        SqliteParameter pSize = cmd.Parameters.Add("$size", SqliteType.Text);
        SqliteParameter pWeight = cmd.Parameters.Add("$weight", SqliteType.Text);
        SqliteParameter pKarat = cmd.Parameters.Add("$karat", SqliteType.Text);
        SqliteParameter pSyncedAt = cmd.Parameters.Add("$syncedAt", SqliteType.Text);
        cmd.Prepare();

        await foreach (JsonElement row in StreamVbscriptAsync("items", ct))
        {
            pKey.Value = row.GetInt("key");
            pTicketKey.Value = row.GetInt("ticket_key");
            pPrinted.Value = NullableVal(row.GetString("printed_detail"));
            pCatCode.Value = NullableVal(row.GetString("category_code"));
            pSerial.Value = NullableVal(row.GetString("serial_no"));
            pCost.Value = NullableVal(row.GetNullableDouble("cost"));
            pItemStatus.Value = NullableVal(row.GetString("item_status"));
            pNotes.Value = NullableVal(row.GetString("notes"));
            pMfg.Value = NullableVal(row.GetString("brand"));
            pModel.Value = NullableVal(row.GetString("model"));
            pColor.Value = NullableVal(row.GetString("color"));
            pSize.Value = NullableVal(row.GetString("size"));
            pWeight.Value = NullableVal(row.GetString("weight"));
            pKarat.Value = NullableVal(row.GetString("metal"));
            pSyncedAt.Value = now;

            await cmd.ExecuteNonQueryAsync(ct);
            count++;

            if (count % 1000 == 0)
                UpdateProgress("items", count, count + 1000, $"Synced {count:N0} items...");
        }

        await txn.CommitAsync(ct);
        return count;
    }

    private async Task<int> SyncPaymentsAsync(SqliteConnection conn, CancellationToken ct)
    {
        string now = DateTime.UtcNow.ToString("o");
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT OR REPLACE INTO PawnPayments
            (Key, TicketKey, PaymentDate, PawnPmtType, PaymentStatus,
             TotalDueAmount, NetDueAmount, NetPaymentAmount, Cash, Check_,
             CreditCard, DebitCard, InterestChargePaid, ServiceChargePaid,
             PrincipalPaid, NewCurrentBalance, NewDueDate, OldDueDate,
             OperatorInitials, Method, Note, SyncedAt)
            VALUES ($key,$ticketKey,$payDate,$pmtType,$payStatus,
             $totalDue,$netDue,$netPay,$cash,$check,
             $creditCard,$debitCard,$intPaid,$svcPaid,
             $princPaid,$newCurBal,$newDueDate,$oldDueDate,
             $opInit,$method,$note,$syncedAt)";

        SqliteParameter pKey = cmd.Parameters.Add("$key", SqliteType.Integer);
        SqliteParameter pTicketKey = cmd.Parameters.Add("$ticketKey", SqliteType.Integer);
        SqliteParameter pPayDate = cmd.Parameters.Add("$payDate", SqliteType.Text);
        SqliteParameter pPmtType = cmd.Parameters.Add("$pmtType", SqliteType.Integer);
        SqliteParameter pPayStatus = cmd.Parameters.Add("$payStatus", SqliteType.Text);
        SqliteParameter pTotalDue = cmd.Parameters.Add("$totalDue", SqliteType.Real);
        SqliteParameter pNetDue = cmd.Parameters.Add("$netDue", SqliteType.Real);
        SqliteParameter pNetPay = cmd.Parameters.Add("$netPay", SqliteType.Real);
        SqliteParameter pCash = cmd.Parameters.Add("$cash", SqliteType.Real);
        SqliteParameter pCheck = cmd.Parameters.Add("$check", SqliteType.Real);
        SqliteParameter pCreditCard = cmd.Parameters.Add("$creditCard", SqliteType.Real);
        SqliteParameter pDebitCard = cmd.Parameters.Add("$debitCard", SqliteType.Real);
        SqliteParameter pIntPaid = cmd.Parameters.Add("$intPaid", SqliteType.Real);
        SqliteParameter pSvcPaid = cmd.Parameters.Add("$svcPaid", SqliteType.Real);
        SqliteParameter pPrincPaid = cmd.Parameters.Add("$princPaid", SqliteType.Real);
        SqliteParameter pNewCurBal = cmd.Parameters.Add("$newCurBal", SqliteType.Real);
        SqliteParameter pNewDueDate = cmd.Parameters.Add("$newDueDate", SqliteType.Text);
        SqliteParameter pOldDueDate = cmd.Parameters.Add("$oldDueDate", SqliteType.Text);
        SqliteParameter pOpInit = cmd.Parameters.Add("$opInit", SqliteType.Text);
        SqliteParameter pMethod = cmd.Parameters.Add("$method", SqliteType.Text);
        SqliteParameter pNote = cmd.Parameters.Add("$note", SqliteType.Text);
        SqliteParameter pSyncedAt = cmd.Parameters.Add("$syncedAt", SqliteType.Text);
        cmd.Prepare();

        await foreach (JsonElement row in StreamVbscriptAsync("payments", ct))
        {
            pKey.Value = row.GetInt("key");
            pTicketKey.Value = row.GetInt("ticket_key");
            pPayDate.Value = NullableVal(row.GetString("payment_date"));
            pPmtType.Value = NullableVal(row.GetNullableInt("pawn_pmt_type"));
            pPayStatus.Value = NullableVal(row.GetString("payment_status"));
            pTotalDue.Value = NullableVal(row.GetNullableDouble("total_due_amount"));
            pNetDue.Value = NullableVal(row.GetNullableDouble("net_due_amount"));
            pNetPay.Value = NullableVal(row.GetNullableDouble("net_payment_amount"));
            pCash.Value = NullableVal(row.GetNullableDouble("cash"));
            pCheck.Value = NullableVal(row.GetNullableDouble("check"));
            pCreditCard.Value = NullableVal(row.GetNullableDouble("credit_card"));
            pDebitCard.Value = NullableVal(row.GetNullableDouble("debit_card"));
            pIntPaid.Value = NullableVal(row.GetNullableDouble("interest_charge_paid"));
            pSvcPaid.Value = NullableVal(row.GetNullableDouble("service_charge_paid"));
            pPrincPaid.Value = NullableVal(row.GetNullableDouble("principal_paid"));
            pNewCurBal.Value = NullableVal(row.GetNullableDouble("new_current_balance"));
            pNewDueDate.Value = NullableVal(row.GetString("new_due_date"));
            pOldDueDate.Value = NullableVal(row.GetString("old_due_date"));
            pOpInit.Value = NullableVal(row.GetString("operator_initials"));
            pMethod.Value = NullableVal(row.GetString("method"));
            pNote.Value = NullableVal(row.GetString("note"));
            pSyncedAt.Value = now;

            await cmd.ExecuteNonQueryAsync(ct);
            count++;

            if (count % 1000 == 0)
                UpdateProgress("payments", count, count + 1000, $"Synced {count:N0} payments...");
        }

        await txn.CommitAsync(ct);
        return count;
    }

    private async Task<int> RebuildPhoneIndexAsync(SqliteConnection conn, AppDbContext db, CancellationToken ct)
    {
        // Load all customers to extract and normalize phones
        List<CustomerEntity> customers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerKey != null)
            .Select(c => new CustomerEntity
            {
                CustomerKey = c.CustomerKey,
                ResPhone = c.ResPhone,
                BusPhone = c.BusPhone
            })
            .ToListAsync(ct);

        int total = customers.Count;
        int count = 0;

        using SqliteTransaction txn = conn.BeginTransaction();

        // Clear existing phone index
        using (SqliteCommand delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = txn;
            delCmd.CommandText = "DELETE FROM CustomerPhones";
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"INSERT OR IGNORE INTO CustomerPhones
            (CustomerKey, PhoneNormalized, PhoneOriginal, PhoneType)
            VALUES ($custKey,$phoneNorm,$phoneOrig,$phoneType)";

        SqliteParameter pCustKey = cmd.Parameters.Add("$custKey", SqliteType.Integer);
        SqliteParameter pPhoneNorm = cmd.Parameters.Add("$phoneNorm", SqliteType.Text);
        SqliteParameter pPhoneOrig = cmd.Parameters.Add("$phoneOrig", SqliteType.Text);
        SqliteParameter pPhoneType = cmd.Parameters.Add("$phoneType", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < customers.Count; i++)
        {
            CustomerEntity customer = customers[i];
            int customerKey = customer.CustomerKey!.Value;

            if (!string.IsNullOrWhiteSpace(customer.ResPhone))
            {
                string? normalized = PhoneUtils.ExtractLast10Digits(customer.ResPhone);
                if (normalized is not null)
                {
                    pCustKey.Value = customerKey;
                    pPhoneNorm.Value = normalized;
                    pPhoneOrig.Value = (object)customer.ResPhone ?? DBNull.Value;
                    pPhoneType.Value = "ResPhone";
                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
            }

            if (!string.IsNullOrWhiteSpace(customer.BusPhone)
                && customer.BusPhone != customer.ResPhone)
            {
                string? normalized = PhoneUtils.ExtractLast10Digits(customer.BusPhone);
                if (normalized is not null)
                {
                    pCustKey.Value = customerKey;
                    pPhoneNorm.Value = normalized;
                    pPhoneOrig.Value = (object)customer.BusPhone ?? DBNull.Value;
                    pPhoneType.Value = "BusPhone";
                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
            }

            if ((i + 1) % 1000 == 0)
                UpdateProgress("phone_index", i + 1, total, $"Indexed {i + 1:N0}/{total:N0} customers...");
        }

        await txn.CommitAsync(ct);
        return count;
    }

    // Null helper: returns DBNull.Value for null, otherwise the value itself.
    private static object NullableVal(object? value) => value ?? DBNull.Value;

    // ── VBScript Streaming ────────────────────────────────────────────

    private async IAsyncEnumerable<JsonElement> StreamVbscriptAsync(
        string queryType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string xpdPath = _currentRunOverrides?.XpdPath ?? _xpdPath;
        string mdwPath = _currentRunOverrides?.MdwPath ?? _mdwPath;
        string xpdUser = _currentRunOverrides?.XpdUser ?? _xpdUser;
        string xpdPassword = _currentRunOverrides?.XpdPassword ?? _xpdPassword;

        ProcessStartInfo startInfo = new()
        {
            FileName = _cscriptPath,
            Arguments = $"//nologo \"{_vbscriptPath}\" \"{xpdPath}\" \"{queryType}\" \"{mdwPath}\" \"{xpdUser}\" \"{xpdPassword}\"",
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
            throw new InvalidOperationException("Failed to start sync process. Check XPD path, MDW path, and that cscript is available.", ex);
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

            if (line.StartsWith("{\"error\"", StringComparison.Ordinal))
            {
                _logger.LogWarning("VBScript error: {Error}", line);
                string errMsg = line;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("error", out JsonElement errProp))
                        errMsg = errProp.GetString() ?? line;
                }
                catch { /* use raw line */ }
                throw new InvalidOperationException($"XPD sync failed: {errMsg}");
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
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"Sync failed (exit code {process.ExitCode}). Check XPD credentials and MDW path."
                : $"Sync failed: {stderr.Trim()}");
        }
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

    private void UpdateProgress(string stage, int current, int total, string message)
    {
        int percent = total > 0 ? (int)((double)current / total * 100) : 0;
        if (percent > 100) percent = 100;
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
