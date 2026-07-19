using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed partial class CustomersController : ControllerBase
{
    private readonly ICustomerRepository _customerRepo;
    private readonly ICustomerAppNoteRepository _customerAppNoteRepo;
    private readonly ITicketRepository _ticketRepo;
    private readonly IIdentityResolver _identityResolver;
    private readonly AppDbContext _db;
    private readonly ILogger<CustomersController> _logger;
    private readonly IConfiguration _configuration;

    public CustomersController(
        ICustomerRepository customerRepo,
        ICustomerAppNoteRepository customerAppNoteRepo,
        ITicketRepository ticketRepo,
        IIdentityResolver identityResolver,
        AppDbContext db,
        ILogger<CustomersController> logger,
        IConfiguration configuration)
    {
        _customerRepo = customerRepo;
        _customerAppNoteRepo = customerAppNoteRepo;
        _ticketRepo = ticketRepo;
        _identityResolver = identityResolver;
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    // GET /api/customers/search?q=john&limit=10
    // Searches customers by name or phone. Scoped to the current user's store.
    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers(
        [FromQuery] string q,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(Array.Empty<object>());

        int storeId;
        if (User.IsHqUser())
        {
            storeId = 0; // HQ can search across all stores
        }
        else
        {
            int? userStoreId = User.GetStoreId();
            if (userStoreId is null)
                return Problem(statusCode: 403, detail: "No store assigned");
            storeId = userStoreId.Value;
        }

        List<Customer> results = await _customerRepo.SearchAsync(storeId, q, limit, cancellationToken);

        List<object> response = results.Select(c => (object)new
        {
            id = c.CustomerId,
            first_name = c.FirstName,
            last_name = c.LastName,
            cell_phone = c.CellPhone,
            home_phone = c.HomePhone,
            store_id = c.StoreId
        }).ToList();

        return Ok(response);
    }

    // POST /api/customer/{customerId}/update
    // Partial update of customer fields (notes, name, tags).
    [HttpPost("customer/{customerId}/update")]
    public async Task<IActionResult> UpdateCustomer(
        int customerId,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        // Lookup customer to check store authorization
        int? userStoreId = User.GetStoreId();
        if (!User.IsHqUser() && userStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        // Search all stores for HQ users, user's store otherwise
        int searchStoreId = User.IsHqUser() ? 0 : userStoreId!.Value;

        // Direct DB lookup since we may not know the store upfront
        Customer? customer = null;
        if (User.IsHqUser())
        {
            // HQ users can update any customer -- try all stores
            var entity = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
            if (entity is not null)
            {
                customer = new Customer
                {
                    CustomerId = entity.CustomerId,
                    StoreId = entity.StoreId
                };
            }
        }
        else
        {
            customer = await _customerRepo.GetByIdAsync(userStoreId!.Value, customerId, cancellationToken);
        }

        if (customer is null)
            return Problem(statusCode: 404, detail: "Customer not found");

        if (!User.CanAccessStore(customer.StoreId))
            return Problem(statusCode: 403, detail: "Cannot edit customer from another store");

        await _customerRepo.UpdateAsync(
            customerId, request.Notes, request.FirstName, request.LastName, request.TagsJson,
            cancellationToken);

        return Ok(new { status = "ok", customer_id = customerId });
    }

    // GET /api/customer/{customerId}/context
    // Full customer context with ticket rollup, active/closed tickets, and notes.
    [HttpGet("customer/{customerId}/context")]
    public async Task<IActionResult> GetCustomerContext(
        int customerId,
        CancellationToken cancellationToken)
    {
        int? userStoreId = User.GetStoreId();
        if (!User.IsHqUser() && userStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        // Load customer
        var customerEntity = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

        if (customerEntity is null)
            return Problem(statusCode: 404, detail: "Customer not found");

        if (!User.CanAccessStore(customerEntity.StoreId))
            return Problem(statusCode: 403, detail: "Access denied");

        // Load tickets from XPD if customer has a CustomerKey
        List<Ticket> allTickets = new();
        if (customerEntity.CustomerKey is not null)
        {
            allTickets = await _ticketRepo.GetByCustomerKeyAsync(
                customerEntity.CustomerKey.Value, cancellationToken);
        }

        // Separate active vs closed tickets
        List<object> activeTickets = new();
        List<object> closedTickets = new();
        int lateCount = 0;
        double totalBalance = 0;
        DateTime today = DateTime.Now;

        foreach (Ticket t in allTickets)
        {
            bool isActive = t.Active == 1;
            object ticketData = new
            {
                ticket_key = t.Key,
                trans_no = t.TransNo,
                amount = t.Amount,
                balance = t.CurrentBalance,
                issue_date = t.IssueDate,
                due_date = t.DueDate,
                date_closed = t.DateClosed,
                how_closed = t.HowClosed,
                items = t.Item ?? "No items"
            };

            if (isActive)
            {
                activeTickets.Add(ticketData);
                totalBalance += t.CurrentBalance ?? 0;

                if (t.DueDate is not null)
                {
                    int? daysLate = PawnCalculator.CalculateDaysLate(
                        TryParseDate(t.DueDate), today);
                    if (daysLate is not null && daysLate.Value > 0)
                        lateCount++;
                }
            }
            else
            {
                closedTickets.Add(ticketData);
            }
        }

        int pfxCount = allTickets.Count(t => t.HowClosed == "PFX-");

        // Calculate payment history using PawnCalculator
        LatePaymentHistory paymentHistory = PawnCalculator.CalculateLatePaymentHistory(allTickets, today);

        // Load XPD notes
        string customerNotes = customerEntity.Notes ?? "";
        string ticketNotes = "";
        string itemNotes = "";

        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            // Customer notes from Customers (XPawn-synced data)
            if (customerEntity.CustomerKey is not null)
            {
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Notes FROM Customers WHERE CustomerKey = @key";
                DbParameter keyParam = cmd.CreateParameter();
                keyParam.ParameterName = "@key";
                keyParam.Value = customerEntity.CustomerKey.Value;
                cmd.Parameters.Add(keyParam);

                object? result = await cmd.ExecuteScalarAsync(cancellationToken);
                if (result is string xpdNotes && !string.IsNullOrWhiteSpace(xpdNotes))
                    customerNotes = xpdNotes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch notes from XPD tables");
        }

        object rollup = new
        {
            active_count = activeTickets.Count,
            late_count = lateCount,
            pfx_count = pfxCount,
            total_balance = totalBalance,
            all_time_count = allTickets.Count,
            ticket_notes = string.IsNullOrEmpty(ticketNotes) ? "No ticket notes." : ticketNotes,
            item_notes = string.IsNullOrEmpty(itemNotes) ? "No item notes." : itemNotes
        };

        string address = string.Join(", ",
            new[] { customerEntity.Address, customerEntity.City, customerEntity.State, customerEntity.Zip }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return Ok(new
        {
            customer = new
            {
                id = customerEntity.CustomerId,
                name = $"{customerEntity.FirstName} {customerEntity.LastName}",
                phone = customerEntity.CellPhone ?? customerEntity.HomePhone,
                xpawn_key = customerEntity.CustomerKey,
                notes = customerNotes,
                since_date = customerEntity.SinceDate?.ToString("o"),
                address
            },
            rollup,
            payment_history = paymentHistory,
            active_tickets = activeTickets,
            closed_tickets = closedTickets
        });
    }

    // POST /api/customers/late
    // Returns customers with late (overdue) pawn tickets, sorted by risk score.
    // Accepts optional SQL query in request body.
    [HttpPost("customers/late")]
    public async Task<IActionResult> GetLateCustomers(
        [FromBody] LateCustomersQueryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        List<object> results = new();

        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            // Use provided query or default
            string sqlQuery = !string.IsNullOrWhiteSpace(request?.Query) 
                ? request.Query 
                : @"
                SELECT
                    c.CustomerKey AS Key,
                    c.CustomerId,
                    c.FirstName,
                    c.LastName,
                    c.ResPhone,
                    c.BusPhone,
                    c.Notes AS CustomerNotes,
                    t.Key AS TicketKey,
                    t.TransNo,
                    t.DueDate,
                    t.CurrentBalance,
                    t.Amount,
                    t.Notes AS TicketNotes,
                    (SELECT COUNT(*) FROM Tickets t2 
                     WHERE t2.CustomerKey = c.CustomerKey 
                     AND t2.HowClosed LIKE 'PFX%') AS ForfeitCount,
                    GROUP_CONCAT(i.PrintedDetail, ' | ') AS Items,
                    GROUP_CONCAT(i.Notes, ' | ') AS ItemNotes,
                    GROUP_CONCAT(i.CategoryCode, ' | ') AS Category
                FROM Tickets t
                JOIN Customers c ON t.CustomerKey = c.CustomerKey
                LEFT JOIN Items i ON i.TicketKey = t.Key
                WHERE t.Type != 0
                  AND t.Active = 1
                  AND t.DueDate IS NOT NULL
                  AND t.DueDate != ''
                GROUP BY t.Key
                ORDER BY t.DueDate DESC
                LIMIT 5000";

            using DbCommand command = connection.CreateCommand();
            command.CommandText = sqlQuery;

            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            DateTime today = DateTime.Now.Date;
            var tempResults = new List<(int RiskScore, object Data)>();

            while (await reader.ReadAsync(cancellationToken))
            {
                string? dueDateStr = reader.IsDBNull(reader.GetOrdinal("DueDate"))
                    ? null : reader.GetString(reader.GetOrdinal("DueDate"));

                DateTime? dueDate = ParseDueDate(dueDateStr);
                if (dueDate is null || dueDate.Value.Date >= today)
                    continue;

                int daysLate = (today - dueDate.Value.Date).Days;
                int customerKey = reader.GetInt32(reader.GetOrdinal("Key"));
                int forfeitCount = reader.IsDBNull(reader.GetOrdinal("ForfeitCount")) 
                    ? 0 : reader.GetInt32(reader.GetOrdinal("ForfeitCount"));
                
                string? category = ExtractFirstCategory(reader.IsDBNull(reader.GetOrdinal("Category")) 
                    ? null : reader.GetString(reader.GetOrdinal("Category")));
                
                int riskScore = CalculateRiskScore(daysLate, forfeitCount, category);
                object data = MapLateCustomerRow(reader, customerKey, dueDateStr, daysLate, forfeitCount, category, riskScore);

                tempResults.Add((riskScore, data));
            }

            results = tempResults.OrderByDescending(r => r.RiskScore)
                .Select(r => r.Data)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying late customers");
        }

        return Ok(results);
    }

    private static DateTime? ParseDueDate(string? dueDateStr)
    {
        if (string.IsNullOrWhiteSpace(dueDateStr))
            return null;

        // Try ISO datetime format first (from SQLite datetime storage)
        if (DateTime.TryParse(dueDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime isoDate))
            return isoDate;

        // Fallback to M/D/YYYY format parsing
        return TryParseDate(dueDateStr);
    }

    private static string? ExtractFirstCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return null;

        return category.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
    }

    private static object MapLateCustomerRow(
        DbDataReader reader,
        int customerKey,
        string? dueDateStr,
        int daysLate,
        int forfeitCount,
        string? category,
        int riskScore)
    {
        int? customerId = reader.IsDBNull(reader.GetOrdinal("CustomerId"))
            ? null : reader.GetInt32(reader.GetOrdinal("CustomerId"));

        int transNo = reader.IsDBNull(reader.GetOrdinal("TransNo")) 
            ? 0 : reader.GetInt32(reader.GetOrdinal("TransNo"));

        List<string> phones = GetCustomerPhones(reader);
        string primaryPhone = phones.Count > 0 ? phones[0] : string.Empty;
        double rawAmount = reader.IsDBNull(reader.GetOrdinal("Amount")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("Amount"));

        return new
        {
            customer_id = customerId,
            customer_key = customerKey,
            first_name = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? "" : reader.GetString(reader.GetOrdinal("FirstName")),
            last_name = reader.IsDBNull(reader.GetOrdinal("LastName")) ? "" : reader.GetString(reader.GetOrdinal("LastName")),
            phone = primaryPhone,
            phones = phones,
            ticket_key = reader.GetInt32(reader.GetOrdinal("TicketKey")),
            trans_no = transNo,
            ticket_no = transNo,
            due_date = dueDateStr,
            days_late = daysLate,
            balance = reader.IsDBNull(reader.GetOrdinal("CurrentBalance")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("CurrentBalance")),
            amount = rawAmount,
            items = reader.IsDBNull(reader.GetOrdinal("Items")) ? "No items" : reader.GetString(reader.GetOrdinal("Items")),
            item_notes = reader.IsDBNull(reader.GetOrdinal("ItemNotes")) ? "" : reader.GetString(reader.GetOrdinal("ItemNotes")),
            customer_notes = reader.IsDBNull(reader.GetOrdinal("CustomerNotes")) ? "" : reader.GetString(reader.GetOrdinal("CustomerNotes")),
            ticket_notes = reader.IsDBNull(reader.GetOrdinal("TicketNotes")) ? "" : reader.GetString(reader.GetOrdinal("TicketNotes")),
            category = category ?? "",
            forfeit_count = forfeitCount,
            risk_score = riskScore,
            risk_band = GetRiskBand(riskScore),
            risk_color = GetRiskColor(riskScore)
        };
    }

    private static List<string> GetCustomerPhones(DbDataReader reader)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> ordered = new List<string>();

        string? res = reader.IsDBNull(reader.GetOrdinal("ResPhone")) ? null : reader.GetString(reader.GetOrdinal("ResPhone"));
        if (!string.IsNullOrWhiteSpace(res))
        {
            string? norm = PhoneUtils.ExtractLast10Digits(res);
            if (norm is not null && seen.Add(norm))
                ordered.Add(norm);
        }

        string? bus = reader.IsDBNull(reader.GetOrdinal("BusPhone")) ? null : reader.GetString(reader.GetOrdinal("BusPhone"));
        if (!string.IsNullOrWhiteSpace(bus))
        {
            string? norm = PhoneUtils.ExtractLast10Digits(bus);
            if (norm is not null && seen.Add(norm))
                ordered.Add(norm);
        }

        string? customerNotes = reader.IsDBNull(reader.GetOrdinal("CustomerNotes")) ? null : reader.GetString(reader.GetOrdinal("CustomerNotes"));
        foreach (string p in PhoneUtils.ExtractPhonesFromText(customerNotes))
        {
            if (seen.Add(p))
                ordered.Add(p);
        }

        string? ticketNotes = reader.IsDBNull(reader.GetOrdinal("TicketNotes")) ? null : reader.GetString(reader.GetOrdinal("TicketNotes"));
        foreach (string p in PhoneUtils.ExtractPhonesFromText(ticketNotes))
        {
            if (seen.Add(p))
                ordered.Add(p);
        }

        return ordered;
    }

    private static int CalculateRiskScore(int daysLate, int forfeitCount, string? category)
    {
        int score = 0;

        // Days late (0-40 points)
        if (daysLate > 90)
            score += 40;
        else if (daysLate > 60)
            score += 30;
        else if (daysLate > 30)
            score += 20;
        else if (daysLate > 0)
            score += 10;

        // Forfeit count (0-40 points)
        if (forfeitCount >= 3)
            score += 40;
        else if (forfeitCount == 2)
            score += 30;
        else if (forfeitCount == 1)
            score += 20;

        // Category risk (0-10 points)
        string? categoryUpper = category?.ToUpperInvariant();
        if (categoryUpper == "ELECTRONICS" || categoryUpper == "GENERAL")
            score += 10;
        else if (categoryUpper == "JEWELRY")
            score += 5;

        return Math.Min(score, 100);
    }

    private static string GetRiskBand(int riskScore)
    {
        if (riskScore >= 70) return "CRITICAL";
        if (riskScore >= 50) return "HIGH";
        if (riskScore >= 30) return "MEDIUM";
        return "LOW";
    }

    private static string GetRiskColor(int riskScore)
    {
        if (riskScore >= 70) return "#d93025"; // Red
        if (riskScore >= 50) return "#f57c00"; // Orange
        if (riskScore >= 30) return "#f9ab00"; // Yellow
        return "#34a853"; // Green
    }


    // GET /api/customers/pfx?days=60
    // Returns customers with PFX (forfeited) tickets in the last N days.
    [HttpGet("customers/pfx")]
    public async Task<IActionResult> GetPfxCustomers(
        [FromQuery] int days = 60,
        CancellationToken cancellationToken = default)
    {
        List<object> results = new();

        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT c.CustomerKey AS Key, c.FirstName, c.LastName, c.ResPhone, c.BusPhone,
                       t.Key AS TicketKey, t.TransNo, t.DateClosed, t.Amount
                FROM Tickets t
                JOIN Customers c ON t.CustomerKey = c.CustomerKey
                WHERE t.HowClosed = 'PFX-'
                  AND t.DateClosed IS NOT NULL
                  AND t.DateClosed >= datetime('now', @daysAgo)
                ORDER BY t.DateClosed DESC";

            DbParameter daysParam = command.CreateParameter();
            daysParam.ParameterName = "@daysAgo";
            daysParam.Value = $"-{days} days";
            command.Parameters.Add(daysParam);

            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new
                {
                    customer_key = reader.GetInt32(reader.GetOrdinal("Key")),
                    first_name = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? "" : reader.GetString(reader.GetOrdinal("FirstName")),
                    last_name = reader.IsDBNull(reader.GetOrdinal("LastName")) ? "" : reader.GetString(reader.GetOrdinal("LastName")),
                    phone = reader.IsDBNull(reader.GetOrdinal("ResPhone"))
                        ? (reader.IsDBNull(reader.GetOrdinal("BusPhone")) ? "" : reader.GetString(reader.GetOrdinal("BusPhone")))
                        : reader.GetString(reader.GetOrdinal("ResPhone")),
                    ticket_key = reader.GetInt32(reader.GetOrdinal("TicketKey")),
                    trans_no = reader.IsDBNull(reader.GetOrdinal("TransNo")) ? 0 : reader.GetInt32(reader.GetOrdinal("TransNo")),
                    date_closed = reader.IsDBNull(reader.GetOrdinal("DateClosed")) ? "" : reader.GetString(reader.GetOrdinal("DateClosed")),
                    amount = reader.IsDBNull(reader.GetOrdinal("Amount")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("Amount"))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying PFX customers");
        }

        return Ok(results);
    }

    // GET /api/test-sqlite?path=...
    // Tests SQLite/XPD table connectivity by counting rows. If path is provided, tests that database; otherwise uses the configured default.
    [HttpGet("test-sqlite")]
    public async Task<IActionResult> TestSqliteConnection(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        try
        {
            DbConnection connection;
            bool ownsConnection = false;

            if (!string.IsNullOrWhiteSpace(path))
            {
                string fullPath = path.Trim();
                var sqliteConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fullPath}");
                await sqliteConn.OpenAsync(cancellationToken);
                connection = sqliteConn;
                ownsConnection = true;
            }
            else
            {
                connection = _db.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken);
            }

            try
            {
                int customers = await CountTable(connection, "Customers", cancellationToken);
                int tickets = await CountTable(connection, "Tickets", cancellationToken);
                int activeTickets = await CountTableWhere(connection, "Tickets", "Active = 1", cancellationToken);
                int items = await CountTable(connection, "Items", cancellationToken);
                int payments = await CountTable(connection, "PawnPayments", cancellationToken);

                return Ok(new
                {
                    success = true,
                    customers,
                    tickets,
                    active_tickets = activeTickets,
                    items,
                    payments
                });
            }
            finally
            {
                if (ownsConnection)
                    await connection.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    // POST /api/customers/append-note-xpd
    // Appends a timestamped note to a customer's Notes field in Customers.
    [Obsolete("Compatibility only. App notes must use /api/customers/{customerKey}/app-notes.")]
    [HttpPost("customers/append-note-xpd")]
    public async Task<IActionResult> AppendNoteToXpd(
        [FromBody] AppendNoteXpdRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
            return Problem(statusCode: 400, detail: "Note cannot be empty");

        string username = User.GetUsername();
        bool isHqUser = User.IsHqUser();
        int? userStoreId = User.GetStoreId();
        if (!isHqUser && userStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        var localCustomer = await _db.Customers
            .FirstOrDefaultAsync(c => c.CustomerKey == request.CustomerKey, cancellationToken);
        if (localCustomer is null)
            return Problem(statusCode: 404, detail: $"Customer Key {request.CustomerKey} not found");

        if (!isHqUser && localCustomer.StoreId != userStoreId!.Value)
            return Problem(statusCode: 403, detail: "Not authorized for this customer");

        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            // Read current notes
            string currentNotes = "";
            using (DbCommand readCmd = connection.CreateCommand())
            {
                readCmd.CommandText = "SELECT Notes FROM Customers WHERE CustomerKey = @key";
                DbParameter keyParam = readCmd.CreateParameter();
                keyParam.ParameterName = "@key";
                keyParam.Value = request.CustomerKey;
                readCmd.Parameters.Add(keyParam);

                object? result = await readCmd.ExecuteScalarAsync(cancellationToken);
                if (result is null || result == DBNull.Value)
                    return Problem(statusCode: 404, detail: $"Customer Key {request.CustomerKey} not found");

                currentNotes = result as string ?? "";
            }

            // Append new note with timestamp and user
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string separator = string.IsNullOrWhiteSpace(currentNotes) ? "" : "\n---\n";
            string updatedNotes = $"{currentNotes}{separator}[{timestamp} - {username}] {request.Note}";

            // Update Customers
            using (DbCommand updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE Customers SET Notes = @notes WHERE CustomerKey = @key";

                DbParameter notesParam = updateCmd.CreateParameter();
                notesParam.ParameterName = "@notes";
                notesParam.Value = updatedNotes;
                updateCmd.Parameters.Add(notesParam);

                DbParameter keyParam2 = updateCmd.CreateParameter();
                keyParam2.ParameterName = "@key";
                keyParam2.Value = request.CustomerKey;
                updateCmd.Parameters.Add(keyParam2);

                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Also update local Customers table
            localCustomer.Notes = updatedNotes;
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                status = "ok",
                message = "Compatibility endpoint updated the local XPD mirror only.",
                note = "This change is not pushed to XPD and may be overwritten by the next XPD sync."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error appending note to customer key {Key}", request.CustomerKey);
            return Problem(statusCode: 500, detail: ex.Message);
        }
    }

    // POST /api/customers/quality
    // Server-side named metrics only (no client-supplied SQL). Late metrics use XpdDateParser in C#.
    [HttpPost("customers/quality")]
    public async Task<IActionResult> GetCustomerQuality(
        [FromBody] CustomerQualityRequest request,
        CancellationToken cancellationToken)
    {
        string metric = string.IsNullOrWhiteSpace(request.QualityMetric)
            ? "default"
            : request.QualityMetric.Trim().ToLowerInvariant();

        if (metric != "default")
            return Problem(statusCode: 400, detail: "Unknown qualityMetric (supported: default)");

        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            int cpuCount = 0;
            int pfxCount = 0;
            using (DbCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT
                        SUM(CASE WHEN TRIM(IFNULL(HowClosed, '')) = 'CPU' THEN 1 ELSE 0 END) AS cpu_count,
                        SUM(CASE WHEN TRIM(IFNULL(HowClosed, '')) = 'PFX-' THEN 1 ELSE 0 END) AS pfx_count
                    FROM Tickets
                    WHERE CustomerKey = @customerKey
                    """;
                DbParameter keyParam = cmd.CreateParameter();
                keyParam.ParameterName = "@customerKey";
                keyParam.Value = request.CustomerKey;
                cmd.Parameters.Add(keyParam);

                using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    int ordCpu = reader.GetOrdinal("cpu_count");
                    int ordPfx = reader.GetOrdinal("pfx_count");
                    if (!reader.IsDBNull(ordCpu))
                        cpuCount = Convert.ToInt32(reader.GetValue(ordCpu));
                    if (!reader.IsDBNull(ordPfx))
                        pfxCount = Convert.ToInt32(reader.GetValue(ordPfx));
                }
            }

            List<Ticket> tickets = await _ticketRepo.GetByCustomerKeyAsync(request.CustomerKey, cancellationToken);
            DateTime today = DateTime.Today;
            int lateTickets = 0;
            double sumDaysLate = 0;
            foreach (Ticket t in tickets)
            {
                if (t.Active != 1 || t.Type == 0)
                    continue;
                if (!XpdDateParser.TryParse(t.DueDate, out DateTime due) || due.Date >= today)
                    continue;
                lateTickets++;
                sumDaysLate += (today - due.Date).TotalDays;
            }

            double avgDaysLate = lateTickets > 0 ? Math.Round(sumDaysLate / lateTickets, 1) : 0;

            var result = new Dictionary<string, object>
            {
                ["cpu_count"] = cpuCount,
                ["pfx_count"] = pfxCount,
                ["late_tickets"] = lateTickets,
                ["avg_days_late"] = avgDaysLate
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error executing customer quality metrics for key {Key}", request.CustomerKey);
            return Ok(new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }

    // Concatenate all non-empty Notes from Items for the given ticket keys, joined with " | ".
    private async Task<string> GetActiveTicketItemNotesAsync(List<int> ticketKeys, CancellationToken cancellationToken)
    {
        if (ticketKeys.Count == 0) return string.Empty;
        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand cmd = connection.CreateCommand();
            List<string> parameterNames = new List<string>(ticketKeys.Count);
            for (int index = 0; index < ticketKeys.Count; index++)
            {
                string parameterName = "@tk" + index;
                parameterNames.Add(parameterName);
                DbParameter parameter = cmd.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.Value = ticketKeys[index];
                cmd.Parameters.Add(parameter);
            }
            cmd.CommandText = "SELECT Notes FROM Items WHERE TicketKey IN (" + string.Join(",", parameterNames) + ") AND Notes IS NOT NULL AND Notes <> ''";

            List<string> noteStrings = new List<string>();
            using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    string noteValue = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(noteValue))
                        noteStrings.Add(noteValue);
                }
            }
            return noteStrings.Count > 0 ? string.Join(" | ", noteStrings) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // Return item descriptions (PrintedDetail) for one ticket, joined with " | ", or "No items" if none.
    private async Task<string> GetTicketItemsTextAsync(int ticketKey, CancellationToken cancellationToken)
    {
        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT PrintedDetail FROM Items WHERE TicketKey = @key";

            DbParameter param = cmd.CreateParameter();
            param.ParameterName = "@key";
            param.Value = ticketKey;
            cmd.Parameters.Add(param);

            List<string> details = new();
            using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    string detail = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(detail))
                        details.Add(detail);
                }
            }

            return details.Count > 0 ? string.Join(" | ", details) : "No items";
        }
        catch
        {
            return "No items";
        }
    }

    // Allowed table names for count queries so we never concatenate user input into table name.
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Customers", "Tickets", "Items", "PawnPayments"
    };

    private static async Task<int> CountTable(DbConnection connection, string tableName, CancellationToken ct)
    {
        if (!AllowedTableNames.Contains(tableName))
            return 0;

        try
        {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}]";
            object? result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<int> CountTableWhere(DbConnection connection, string tableName, string where, CancellationToken ct)
    {
        if (!AllowedTableNames.Contains(tableName))
            return 0;

        try
        {
            using DbCommand cmd = connection.CreateCommand();
            // where clause is only called with hardcoded values from TestSqliteConnection.
            cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}] WHERE {where}";
            object? result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? TryParseDate(string? dateString) =>
        XpdDateParser.TryParse(dateString, out DateTime d) ? d : null;
}

// Request body for POST /api/customers/append-note-xpd.
public sealed class AppendNoteXpdRequest
{
    public int CustomerKey { get; set; }
    public string Note { get; set; } = string.Empty;
}

// Request body for POST /api/customers/late.
public sealed class LateCustomersQueryRequest
{
    public string? Query { get; set; }
}

// Request body for POST /api/customers/quality.
public sealed class CustomerQualityRequest
{
    public int CustomerKey { get; set; }
    public string? QualityMetric { get; set; }
}
