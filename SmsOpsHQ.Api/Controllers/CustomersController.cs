using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerRepository _customerRepo;
    private readonly ITicketRepository _ticketRepo;
    private readonly IIdentityResolver _identityResolver;
    private readonly AppDbContext _db;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        ICustomerRepository customerRepo,
        ITicketRepository ticketRepo,
        IIdentityResolver identityResolver,
        AppDbContext db,
        ILogger<CustomersController> logger)
    {
        _customerRepo = customerRepo;
        _ticketRepo = ticketRepo;
        _identityResolver = identityResolver;
        _db = db;
        _logger = logger;
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

        int pfxCount = allTickets.Count(t =>
            t.HowClosed is not null && t.HowClosed.StartsWith("PFX", StringComparison.OrdinalIgnoreCase));

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

        return new
        {
            customer_id = customerId,
            customer_key = customerKey,
            first_name = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? "" : reader.GetString(reader.GetOrdinal("FirstName")),
            last_name = reader.IsDBNull(reader.GetOrdinal("LastName")) ? "" : reader.GetString(reader.GetOrdinal("LastName")),
            phone = GetCustomerPhone(reader),
            ticket_key = reader.GetInt32(reader.GetOrdinal("TicketKey")),
            trans_no = transNo,
            ticket_no = transNo,
            due_date = dueDateStr,
            days_late = daysLate,
            balance = reader.IsDBNull(reader.GetOrdinal("CurrentBalance")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("CurrentBalance")),
            amount = reader.IsDBNull(reader.GetOrdinal("Amount")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("Amount")) / 1000.0,
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

    private static string GetCustomerPhone(DbDataReader reader)
    {
        if (!reader.IsDBNull(reader.GetOrdinal("ResPhone")))
            return reader.GetString(reader.GetOrdinal("ResPhone"));
        
        if (!reader.IsDBNull(reader.GetOrdinal("BusPhone")))
            return reader.GetString(reader.GetOrdinal("BusPhone"));
        
        return "";
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

    // GET /api/customer/by-phone?phone=9294990435
    // Full identity resolution: phone -> CustomerKeys -> customer + tickets + quality score.
    [HttpGet("customer/by-phone")]
    public async Task<IActionResult> GetCustomerByPhone(
        [FromQuery] string phone,
        CancellationToken cancellationToken)
    {
        string? normalizedPhone = PhoneUtils.ExtractLast10Digits(phone);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            return Ok(new
            {
                found = false, customer = (object?)null, stats = (object?)null,
                quality = (object?)null, active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(), pfx_tickets = Array.Empty<object>(),
                error = "Invalid phone number format"
            });
        }

        // Step 1: Resolve ALL CustomerKeys from phone index
        List<int> customerKeys = await _identityResolver.ResolveCustomerKeysAsync(
            phone, cancellationToken);

        if (customerKeys.Count == 0)
        {
            return Ok(new
            {
                found = false, customer = (object?)null, stats = (object?)null,
                quality = (object?)null, active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(), pfx_tickets = Array.Empty<object>()
            });
        }

        int primaryCustomerKey = customerKeys[0];

        // Step 2: Load customer info from Customers table
        object? customerData = null;
        try
        {
            DbConnection connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT FirstName, LastName, MiddleName, Address, City, State, Zip,
                       ResPhone, BusPhone, EMailAddress, Notes, FirstTransaction, LastTransaction,
                       DOB, SSN, IDNo, IDIssueState, Warning
                FROM Customers WHERE CustomerKey = @key";

            DbParameter keyParam = cmd.CreateParameter();
            keyParam.ParameterName = "@key";
            keyParam.Value = primaryCustomerKey;
            cmd.Parameters.Add(keyParam);

            using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                string ReadStringOrEmpty(string col) =>
                    reader.IsDBNull(reader.GetOrdinal(col)) ? "" : reader.GetString(reader.GetOrdinal(col));

                customerData = new
                {
                    key = primaryCustomerKey,
                    all_keys = customerKeys,
                    first_name = ReadStringOrEmpty("FirstName"),
                    last_name = ReadStringOrEmpty("LastName"),
                    middle_name = ReadStringOrEmpty("MiddleName"),
                    address = ReadStringOrEmpty("Address"),
                    city = ReadStringOrEmpty("City"),
                    state = ReadStringOrEmpty("State"),
                    zip = ReadStringOrEmpty("Zip"),
                    res_phone = ReadStringOrEmpty("ResPhone"),
                    bus_phone = ReadStringOrEmpty("BusPhone"),
                    email = ReadStringOrEmpty("EMailAddress"),
                    notes = ReadStringOrEmpty("Notes"),
                    first_transaction = ReadStringOrEmpty("FirstTransaction"),
                    last_transaction = ReadStringOrEmpty("LastTransaction"),
                    warning = ReadStringOrEmpty("Warning"),
                    phone = ReadStringOrEmpty("ResPhone").Length > 0
                        ? ReadStringOrEmpty("ResPhone") : ReadStringOrEmpty("BusPhone")
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading XPD customer for key {Key}", primaryCustomerKey);
        }

        if (customerData is null)
        {
            return Ok(new
            {
                found = false, customer = (object?)null, stats = (object?)null,
                quality = (object?)null, active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(), pfx_tickets = Array.Empty<object>()
            });
        }

        // Resolve app CustomerId when this phone is linked to a local customer
        int? appCustomerId = null;
        var appCustomer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerKey == primaryCustomerKey, cancellationToken);
        if (appCustomer is not null)
            appCustomerId = appCustomer.CustomerId;

        // Step 3: Load tickets for ALL customer keys
        List<Ticket> allTickets = await _ticketRepo.GetByCustomerKeysAsync(customerKeys, cancellationToken);

        // Filter to relevant tickets: active OR closed CPU OR closed PFX
        List<object> activeTickets = new();
        List<object> cpuTickets = new();
        List<object> pfxTickets = new();
        int lateCount = 0;
        double totalBalance = 0;
        List<int> cpuOverdueDays = new();
        DateTime today = DateTime.Now;

        foreach (Ticket t in allTickets)
        {
            bool isActive = t.Active == 1;
            bool isCpu = !isActive && t.HowClosed == "CPU";
            bool isPfx = !isActive && t.HowClosed == "PFX-";

            if (!isActive && !isCpu && !isPfx)
                continue;

            // Load items for this ticket
            string itemsText = await GetTicketItemsTextAsync(t.Key, cancellationToken);

            object ticketData = new
            {
                ticket_key = t.Key,
                customer_key = t.CustomerKey,
                trans_no = t.TransNo,
                amount = t.Amount ?? 0,
                balance = t.CurrentBalance ?? 0,
                issue_date = t.IssueDate,
                due_date = t.DueDate,
                date_closed = t.DateClosed,
                how_closed = t.HowClosed,
                items = itemsText,
                status = isActive ? "ACTIVE" : (isCpu ? "CLOSED_CPU" : "CLOSED_PFX")
            };

            if (isActive)
            {
                activeTickets.Add(ticketData);
                totalBalance += t.CurrentBalance ?? 0;

                DateTime? dueDate = TryParseDate(t.DueDate);
                if (dueDate is not null && dueDate.Value.Date < today.Date)
                    lateCount++;
            }
            else if (isCpu)
            {
                int overdueDays = PawnCalculator.CalculateCpuOverdueDays(
                    TryParseDate(t.DateClosed), TryParseDate(t.DueDate));
                cpuOverdueDays.Add(overdueDays);
                cpuTickets.Add(ticketData);
            }
            else if (isPfx)
            {
                pfxTickets.Add(ticketData);
            }
        }

        // Step 4: Calculate stats and quality score
        object stats = new
        {
            active_count = activeTickets.Count,
            late_count = lateCount,
            pfx_count = pfxTickets.Count,
            cpu_count = cpuTickets.Count,
            total_balance = totalBalance,
            all_time_count = allTickets.Count
        };

        CpuScoreResult cpuScore = PawnCalculator.CalculateCpuScore(cpuOverdueDays, pfxTickets.Count);

        object quality = new
        {
            score = cpuScore.Score,
            level = cpuScore.Level,
            color = cpuScore.Color,
            cpu_score = cpuScore.CpuScore,
            pfx_penalty = cpuScore.PfxPenalty,
            severe_overdue = cpuScore.SevereOverdue
        };

        // Step 5: Calculate late payment history
        LatePaymentHistory paymentHistory = PawnCalculator.CalculateLatePaymentHistory(allTickets, today);

        return Ok(new
        {
            found = true,
            customer = customerData,
            customer_id = appCustomerId,
            stats,
            quality,
            payment_history = paymentHistory,
            active_tickets = activeTickets,
            cpu_tickets = cpuTickets,
            pfx_tickets = pfxTickets
        });
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
    [HttpPost("customers/append-note-xpd")]
    public async Task<IActionResult> AppendNoteToXpd(
        [FromBody] AppendNoteXpdRequest request,
        CancellationToken cancellationToken)
    {
        string username = User.GetUsername();

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
            var localCustomer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CustomerKey == request.CustomerKey, cancellationToken);
            if (localCustomer is not null)
            {
                localCustomer.Notes = updatedNotes;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                status = "ok",
                message = "Note saved to local database. Run XPD sync to update the XPD file.",
                note = "Changes are in local SQLite only until next XPD sync"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error appending note to customer key {Key}", request.CustomerKey);
            return Problem(statusCode: 500, detail: ex.Message);
        }
    }

    // Reads item descriptions for a given ticket from Items table.
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

    // Whitelist of allowed table names to prevent SQL injection.
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

    private static readonly string[] XpdDateFormats = { 
        "M/d/yyyy", "MM/dd/yyyy", "M/dd/yyyy", "MM/d/yyyy",
        "yyyy-MM-dd", "yyyy-M-d", "yyyy-MM-d", "yyyy-M-dd"
    };

    private static DateTime? TryParseDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;

        string datePart = dateString.Contains(' ') ? dateString.Split(' ')[0].Trim() : dateString.Trim();

        // Try exact formats first
        if (DateTime.TryParseExact(datePart, XpdDateFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
        {
            return result;
        }

        // Fallback: Manual parsing like Python code (M/D/YYYY format)
        if (datePart.Contains('/'))
        {
            string[] parts = datePart.Split('/');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int month) &&
                int.TryParse(parts[1], out int day) &&
                int.TryParse(parts[2], out int year))
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    // Invalid date (e.g., Feb 30)
                }
            }
        }

        return null;
    }
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
