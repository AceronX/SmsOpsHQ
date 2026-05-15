using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Models;
using SmsOpsHQ.Core.Utilities;
namespace SmsOpsHQ.Api.Controllers;

// Identity-safe /api/customer/by-phone: profile and ticket risk use the same verified CustomerKey set.
public sealed partial class CustomersController
{
    private sealed class XpdCustomerProfile
    {
        public int CustomerKey { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string MiddleName { get; set; } = "";
        public string Address { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Zip { get; set; } = "";
        public string ResPhone { get; set; } = "";
        public string BusPhone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Notes { get; set; } = "";
        public string FirstTransaction { get; set; } = "";
        public string LastTransaction { get; set; } = "";
        public string Dob { get; set; } = "";
        public string Ssn { get; set; } = "";
        public string IdNo { get; set; } = "";
        public string IdIssueState { get; set; } = "";
        public string Warning { get; set; } = "";

        public bool HasDirectPhoneMatch { get; set; }
        public string PhoneMatchSource { get; set; } = "";
        public string PhoneMatchType { get; set; } = "";
        public int MatchRank { get; set; }
        public int MatchScore { get; set; }
        public int ActiveTicketCount { get; set; }
    }

    [HttpGet("customer/by-phone")]
    public async Task<IActionResult> GetCustomerByPhone(
        [FromQuery] string phone,
        [FromQuery] int? selectedCustomerKey,
        CancellationToken cancellationToken)
    {
        bool isHqUser = User.IsHqUser();
        int? userStoreId = User.GetStoreId();
        if (!isHqUser && userStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        string? normalizedPhone = PhoneUtils.ExtractLast10Digits(phone);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            return Ok(new
            {
                found = false,
                ambiguous = false,
                customer = (object?)null,
                stats = (object?)null,
                quality = (object?)null,
                active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(),
                pfx_tickets = Array.Empty<object>(),
                error = "Invalid phone number format"
            });
        }

        List<CustomerPhoneMatch> matches = await _identityResolver.ResolveCustomerPhoneMatchesAsync(phone, cancellationToken);

        if (!isHqUser)
        {
            List<int> matchKeys = matches.Select(m => m.CustomerKey).Distinct().ToList();
            List<int> allowedKeys = await _db.Customers
                .AsNoTracking()
                .Where(c => c.StoreId == userStoreId!.Value &&
                            c.CustomerKey.HasValue &&
                            matchKeys.Contains(c.CustomerKey.Value))
                .Select(c => c.CustomerKey!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            matches = matches.Where(m => allowedKeys.Contains(m.CustomerKey)).ToList();
        }

        if (matches.Count == 0)
        {
            return Ok(new
            {
                found = false,
                ambiguous = false,
                customer = (object?)null,
                stats = (object?)null,
                quality = (object?)null,
                active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(),
                pfx_tickets = Array.Empty<object>(),
                error = "No customer found for this store"
            });
        }

        List<int> distinctKeys = matches.Select(m => m.CustomerKey).Distinct().OrderBy(k => k).ToList();
        Dictionary<int, XpdCustomerProfile> profiles = await LoadXpdCustomerProfilesAsync(distinctKeys, cancellationToken);
        Dictionary<int, (int ActiveTicketCount, string? LastIssueDate)> ticketInfo =
            await GetTicketSummariesByCustomerKeysAsync(distinctKeys, cancellationToken);

        List<XpdCustomerProfile> candidates = BuildCandidates(matches, profiles, ticketInfo);
        if (candidates.Count == 0)
        {
            return Ok(new
            {
                found = false,
                ambiguous = false,
                customer = (object?)null,
                stats = (object?)null,
                quality = (object?)null,
                active_tickets = Array.Empty<object>(),
                cpu_tickets = Array.Empty<object>(),
                pfx_tickets = Array.Empty<object>()
            });
        }

        var appRows = await _db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerKey != null && distinctKeys.Contains(c.CustomerKey.Value))
            .Select(c => new { Key = c.CustomerKey!.Value, c.CustomerId })
            .ToListAsync(cancellationToken);

        Dictionary<int, int> appCustomerIdByKey = appRows
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().CustomerId);

        if (selectedCustomerKey is int userPick)
        {
            XpdCustomerProfile? picked = candidates.FirstOrDefault(c => c.CustomerKey == userPick);
            if (picked is null)
            {
                return Ok(new
                {
                    found = false,
                    ambiguous = false,
                    error = "Selected customer is not in the match set for this phone.",
                    customer = (object?)null,
                    stats = (object?)null,
                    quality = (object?)null,
                    active_tickets = Array.Empty<object>(),
                    cpu_tickets = Array.Empty<object>(),
                    pfx_tickets = Array.Empty<object>()
                });
            }

            List<int> merged = MergeWithVerifiedDuplicates(userPick, candidates, profiles, appCustomerIdByKey);
            return await BuildResolvedByPhoneResponseAsync(
                picked,
                merged,
                "user_selected",
                Array.Empty<object>(),
                riskDataSuppressed: false,
                cancellationToken);
        }

        List<int> directKeys = candidates.Where(c => c.HasDirectPhoneMatch).Select(c => c.CustomerKey).Distinct().OrderBy(k => k).ToList();

        if (directKeys.Count == 1)
        {
            int primary = directKeys[0];
            XpdCustomerProfile primaryProfile = profiles[primary];
            List<int> merged = MergeWithVerifiedDuplicates(primary, candidates, profiles, appCustomerIdByKey);
            return await BuildResolvedByPhoneResponseAsync(
                primaryProfile,
                merged,
                "direct",
                Array.Empty<object>(),
                riskDataSuppressed: false,
                cancellationToken);
        }

        if (directKeys.Count >= 2)
        {
            if (MutualDuplicateClique(directKeys, profiles, appCustomerIdByKey))
            {
                int primary = directKeys[0];
                List<int> merged = directKeys;
                return await BuildResolvedByPhoneResponseAsync(
                    profiles[primary],
                    merged,
                    "direct",
                    Array.Empty<object>(),
                    riskDataSuppressed: false,
                    cancellationToken);
            }

            return Ok(BuildAmbiguousResponse(candidates));
        }

        // Notes-only (no direct phone on file for this number)
        if (candidates.Count == 1)
        {
            XpdCustomerProfile c = candidates[0];
            int? appId = appCustomerIdByKey.GetValueOrDefault(c.CustomerKey);
            object customerJson = BuildCustomerJson(c, appId, mergedKeys: Array.Empty<int>());
            return Ok(new
            {
                found = true,
                ambiguous = false,
                match_confidence = "note_reference_only",
                risk_data_suppressed = true,
                selected_customer_key = c.CustomerKey,
                merged_customer_keys = Array.Empty<int>(),
                candidate_customers = Array.Empty<object>(),
                customer = customerJson,
                customer_id = appId,
                active_tickets = Array.Empty<object>(),
                stats = (object?)null,
                quality = (object?)null,
                payment_history = (object?)null,
                decision_card = (object?)null,
                ticket_notes = "",
                item_notes = "",
                cpu_tickets = Array.Empty<object>(),
                pfx_tickets = Array.Empty<object>()
            });
        }

        return Ok(BuildAmbiguousResponse(candidates));
    }

    private static object BuildAmbiguousResponse(List<XpdCustomerProfile> candidates) =>
        new
        {
            found = true,
            ambiguous = true,
            error = "Multiple customers match this phone number. Select the correct client before showing ticket risk.",
            selected_customer_key = (int?)null,
            merged_customer_keys = Array.Empty<int>(),
            candidate_customers = candidates
                .OrderByDescending(c => c.MatchScore)
                .Select(c => new
                {
                    customer_key = c.CustomerKey,
                    name = $"{c.FirstName} {c.LastName}".Trim(),
                    phone_match_source = c.PhoneMatchSource,
                    phone_match_type = c.PhoneMatchType,
                    phone_match_is_direct = c.HasDirectPhoneMatch,
                    match_score = c.MatchScore,
                    last_transaction = c.LastTransaction,
                    active_ticket_count = c.ActiveTicketCount
                })
                .ToList(),
            customer = (object?)null,
            active_tickets = Array.Empty<object>(),
            stats = (object?)null,
            quality = (object?)null,
            payment_history = (object?)null,
            decision_card = (object?)null,
            ticket_notes = "",
            item_notes = "",
            cpu_tickets = Array.Empty<object>(),
            pfx_tickets = Array.Empty<object>()
        };

    private async Task<IActionResult> BuildResolvedByPhoneResponseAsync(
        XpdCustomerProfile primaryProfile,
        List<int> mergedCustomerKeys,
        string matchConfidence,
        IReadOnlyList<object> extraCandidates,
        bool riskDataSuppressed,
        CancellationToken cancellationToken)
    {
        int? appCustomerId = await _db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerKey == primaryProfile.CustomerKey)
            .Select(c => (int?)c.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        object customerJson = BuildCustomerJson(primaryProfile, appCustomerId, mergedCustomerKeys);
        DateTime today = DateTime.Now;

        List<Ticket> allTickets = await _ticketRepo.GetByCustomerKeysAsync(mergedCustomerKeys, cancellationToken);

        List<object> activeTickets = new();
        List<object> cpuTickets = new();
        List<object> pfxTickets = new();
        List<int> cpuOverdueDays = new();

        foreach (Ticket t in allTickets)
        {
            string howClosed = (t.HowClosed ?? "").Trim();
            bool isActive = t.Active == 1 && t.Type != 0;
            bool isCpu = !isActive && PawnCalculator.IsCpuHowClosed(howClosed);
            bool isPfx = !isActive && PawnCalculator.IsPfxHowClosed(howClosed);

            if (!isActive && !isCpu && !isPfx)
                continue;

            string itemsText = await GetTicketItemsTextAsync(t.Key, cancellationToken);
            object ticketData = new
            {
                ticket_key = t.Key,
                customer_key = t.CustomerKey,
                trans_no = t.TransNo,
                type = t.Type ?? 1,
                amount = t.Amount ?? 0,
                balance = t.CurrentBalance ?? 0,
                standard_pu = t.StandardPU ?? 0,
                grace_pu = t.FullTermPU ?? 0,
                renew_amount = t.FulltermRenew ?? 0,
                issue_date = t.IssueDate,
                due_date = t.DueDate,
                date_closed = t.DateClosed,
                how_closed = t.HowClosed,
                items = itemsText,
                status = isActive ? "ACTIVE" : (isCpu ? "CLOSED_CPU" : "CLOSED_PFX")
            };

            if (isActive)
                activeTickets.Add(ticketData);
            else if (isCpu)
            {
                int overdueDays = PawnCalculator.CalculateCpuOverdueDays(
                    TryParseDate(t.DateClosed), TryParseDate(t.DueDate));
                cpuOverdueDays.Add(overdueDays);
                cpuTickets.Add(ticketData);
            }
            else if (isPfx)
                pfxTickets.Add(ticketData);
        }

        object stats = BuildStatsFromTickets(allTickets, today);
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

        LatePaymentHistory paymentHistory = PawnCalculator.CalculateLatePaymentHistory(allTickets, today);
        var profileFlags = new ProfileFlags
        {
            HasID = !string.IsNullOrWhiteSpace(primaryProfile.IdNo),
            HasAddress = !string.IsNullOrWhiteSpace(primaryProfile.Address),
            HasContact = !string.IsNullOrWhiteSpace(primaryProfile.ResPhone) || !string.IsNullOrWhiteSpace(primaryProfile.BusPhone)
        };
        DecisionCardResult decisionCard = PawnCalculator.CalculateDecisionCard(allTickets, profileFlags, today);

        List<string> ticketNotesList = new();
        List<int> activeTicketKeys = new();
        foreach (Ticket t in allTickets.Where(t => t.Active == 1 && t.Type != 0))
        {
            activeTicketKeys.Add(t.Key);
            if (!string.IsNullOrWhiteSpace(t.Notes))
                ticketNotesList.Add($"Ticket #{t.TransNo}: {t.Notes}");
        }

        string ticketNotesStr = ticketNotesList.Count > 0 ? string.Join("\n", ticketNotesList) : "";
        string itemNotesStr = await GetActiveTicketItemNotesAsync(activeTicketKeys, cancellationToken);

        return Ok(new
        {
            found = true,
            ambiguous = false,
            match_confidence = matchConfidence,
            risk_data_suppressed = riskDataSuppressed,
            selected_customer_key = primaryProfile.CustomerKey,
            merged_customer_keys = mergedCustomerKeys,
            candidate_customers = extraCandidates,
            customer = customerJson,
            customer_id = appCustomerId,
            stats,
            quality,
            payment_history = paymentHistory,
            decision_card = decisionCard,
            ticket_notes = ticketNotesStr,
            item_notes = itemNotesStr,
            active_tickets = activeTickets,
            cpu_tickets = cpuTickets,
            pfx_tickets = pfxTickets
        });
    }

    private static object BuildStatsFromTickets(List<Ticket> tickets, DateTime today)
    {
        int lateCount = 0;
        int pfxCount = 0;
        int cpuCount = 0;
        double totalBalance = 0;
        int activeCount = 0;

        foreach (Ticket ticket in tickets)
        {
            string howClosed = (ticket.HowClosed ?? "").Trim();
            bool isActive = ticket.Active == 1 && ticket.Type != 0;

            if (isActive)
            {
                activeCount++;
                totalBalance += ticket.CurrentBalance ?? 0;
                if (XpdDateParser.TryParse(ticket.DueDate, out DateTime dueDate) && dueDate.Date < today.Date)
                    lateCount++;
                continue;
            }

            if (PawnCalculator.IsCpuHowClosed(howClosed))
            {
                cpuCount++;
                continue;
            }

            if (PawnCalculator.IsPfxHowClosed(howClosed))
                pfxCount++;
        }

        return new
        {
            active_count = activeCount,
            late_count = lateCount,
            pfx_count = pfxCount,
            cpu_count = cpuCount,
            total_balance = totalBalance,
            all_time_count = tickets.Count
        };
    }

    private object BuildCustomerJson(XpdCustomerProfile p, int? customerId, IReadOnlyList<int> mergedKeys)
    {
        string name = $"{p.FirstName} {p.LastName}".Trim();
        if (string.IsNullOrEmpty(name)) name = "Unknown";

        return new
        {
            key = p.CustomerKey,
            customer_key = p.CustomerKey,
            merged_customer_keys = mergedKeys,
            first_name = p.FirstName,
            middle_name = p.MiddleName,
            last_name = p.LastName,
            name,
            address = p.Address,
            city = p.City,
            state = p.State,
            zip = p.Zip,
            res_phone = p.ResPhone,
            bus_phone = p.BusPhone,
            phone = !string.IsNullOrEmpty(p.ResPhone) ? p.ResPhone : p.BusPhone,
            email = p.Email,
            notes = p.Notes,
            first_transaction = p.FirstTransaction,
            last_transaction = p.LastTransaction,
            dob = p.Dob,
            id_no = p.IdNo,
            id_issue_state = p.IdIssueState,
            ssn_last4 = SsnLast4(p.Ssn),
            warning = p.Warning,
            id_photo_available = IdPhotoAvailableForProfile(p)
        };
    }

    private static string SsnLast4(string? ssn)
    {
        if (string.IsNullOrWhiteSpace(ssn)) return "";
        string digits = new string(ssn.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : "";
    }

    private static List<XpdCustomerProfile> BuildCandidates(
        List<CustomerPhoneMatch> matches,
        Dictionary<int, XpdCustomerProfile> profiles,
        Dictionary<int, (int ActiveTicketCount, string? LastIssueDate)> ticketInfo)
    {
        var list = new List<XpdCustomerProfile>();
        foreach (int key in matches.Select(m => m.CustomerKey).Distinct())
        {
            if (!profiles.TryGetValue(key, out XpdCustomerProfile? p))
                continue;

            List<CustomerPhoneMatch> keyMatches = matches.Where(m => m.CustomerKey == key).ToList();
            CustomerPhoneMatch best = keyMatches.OrderByDescending(m => m.MatchRank).First();
            p.HasDirectPhoneMatch = keyMatches.Any(m => m.IsDirect);
            p.PhoneMatchSource = best.SourceField;
            p.PhoneMatchType = best.MatchType;
            p.MatchRank = best.MatchRank;
            if (ticketInfo.TryGetValue(key, out var ti))
                p.ActiveTicketCount = ti.ActiveTicketCount;
            p.MatchScore = ComputeMatchScore(p);
            list.Add(p);
        }

        return list;
    }

    private static int ComputeMatchScore(XpdCustomerProfile p)
    {
        int s = p.MatchRank;
        if (p.ActiveTicketCount > 0) s += 10;
        if (XpdDateParser.TryParse(p.LastTransaction, out DateTime lt) && lt > DateTime.Now.AddMonths(-18))
            s += 10;
        if (!string.IsNullOrWhiteSpace(p.IdNo)) s += 5;
        if (!string.IsNullOrWhiteSpace(p.Dob)) s += 5;
        return s;
    }

    private static List<int> MergeWithVerifiedDuplicates(
        int primary,
        List<XpdCustomerProfile> candidates,
        Dictionary<int, XpdCustomerProfile> profiles,
        Dictionary<int, int> appCustomerIdByKey)
    {
        var set = new HashSet<int> { primary };
        XpdCustomerProfile a = profiles[primary];
        foreach (XpdCustomerProfile b in candidates)
        {
            if (b.CustomerKey == primary) continue;
            if (AreVerifiedDuplicateRecords(a, b, appCustomerIdByKey))
                set.Add(b.CustomerKey);
        }

        return set.OrderBy(k => k).ToList();
    }

    private static bool MutualDuplicateClique(
        List<int> keys,
        Dictionary<int, XpdCustomerProfile> profiles,
        Dictionary<int, int> appCustomerIdByKey)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = i + 1; j < keys.Count; j++)
            {
                if (!AreVerifiedDuplicateRecords(profiles[keys[i]], profiles[keys[j]], appCustomerIdByKey))
                    return false;
            }
        }

        return true;
    }

    private static bool AreVerifiedDuplicateRecords(
        XpdCustomerProfile a,
        XpdCustomerProfile b,
        IReadOnlyDictionary<int, int> appCustomerIdByKey)
    {
        if (a.CustomerKey == b.CustomerKey)
            return true;

        if (appCustomerIdByKey.TryGetValue(a.CustomerKey, out int ida) &&
            appCustomerIdByKey.TryGetValue(b.CustomerKey, out int idb) &&
            ida == idb)
            return true;

        string na = NormalizeId(a.IdNo);
        string nb = NormalizeId(b.IdNo);
        if (!string.IsNullOrEmpty(na) && na == nb &&
            string.Equals(NormalizeState(a.IdIssueState), NormalizeState(b.IdIssueState), StringComparison.OrdinalIgnoreCase))
            return true;

        if (XpdDateParser.TryParse(a.Dob, out DateTime da) &&
            XpdDateParser.TryParse(b.Dob, out DateTime db) &&
            da.Date == db.Date &&
            NormalizeName(a.LastName) == NormalizeName(b.LastName) &&
            NormalizeName(a.FirstName) == NormalizeName(b.FirstName) &&
            NormalizeAddr(a.Address) == NormalizeAddr(b.Address))
            return true;

        return false;
    }

    private static string NormalizeId(string? id) => (id ?? "").Trim().ToUpperInvariant();
    private static string NormalizeState(string? s) => (s ?? "").Trim();
    private static string NormalizeName(string? s) => string.Join(' ', (s ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string NormalizeAddr(string? s) => string.Join(' ', (s ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private async Task<Dictionary<int, XpdCustomerProfile>> LoadXpdCustomerProfilesAsync(
        List<int> keys,
        CancellationToken cancellationToken)
    {
        var dict = new Dictionary<int, XpdCustomerProfile>();
        if (keys.Count == 0)
            return dict;

        DbConnection connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using DbCommand cmd = connection.CreateCommand();
        string placeholders = string.Join(",", keys.Select((_, i) => "@k" + i));
        cmd.CommandText = $@"
            SELECT CustomerKey, FirstName, LastName, MiddleName, Address, City, State, Zip,
                   ResPhone, BusPhone, EMailAddress, Notes, FirstTransaction, LastTransaction,
                   DOB, SSN, IDNo, IDIssueState, Warning
            FROM Customers WHERE CustomerKey IN ({placeholders})";

        for (int i = 0; i < keys.Count; i++)
        {
            DbParameter param = cmd.CreateParameter();
            param.ParameterName = "@k" + i;
            param.Value = keys[i];
            cmd.Parameters.Add(param);
        }

        using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string Read(string col) =>
                reader.IsDBNull(reader.GetOrdinal(col)) ? "" : reader.GetString(reader.GetOrdinal(col));

            int ck = reader.GetInt32(reader.GetOrdinal("CustomerKey"));
            dict[ck] = new XpdCustomerProfile
            {
                CustomerKey = ck,
                FirstName = Read("FirstName"),
                LastName = Read("LastName"),
                MiddleName = Read("MiddleName"),
                Address = Read("Address"),
                City = Read("City"),
                State = Read("State"),
                Zip = Read("Zip"),
                ResPhone = Read("ResPhone"),
                BusPhone = Read("BusPhone"),
                Email = Read("EMailAddress"),
                Notes = Read("Notes"),
                FirstTransaction = Read("FirstTransaction"),
                LastTransaction = Read("LastTransaction"),
                Dob = Read("DOB"),
                Ssn = Read("SSN"),
                IdNo = Read("IDNo"),
                IdIssueState = Read("IDIssueState"),
                Warning = Read("Warning")
            };
        }

        return dict;
    }

    private async Task<Dictionary<int, (int ActiveTicketCount, string? LastIssueDate)>> GetTicketSummariesByCustomerKeysAsync(
        List<int> keys,
        CancellationToken cancellationToken)
    {
        var dict = new Dictionary<int, (int, string?)>();
        if (keys.Count == 0)
            return dict;

        DbConnection connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using DbCommand cmd = connection.CreateCommand();
        string placeholders = string.Join(",", keys.Select((_, i) => "@k" + i));
        cmd.CommandText = $@"
            SELECT CustomerKey,
                   SUM(CASE WHEN Active = 1 AND IFNULL(Type, 1) != 0 THEN 1 ELSE 0 END) AS ActiveTicketCount,
                   MAX(IssueDate) AS LastIssueDate
            FROM Tickets
            WHERE CustomerKey IN ({placeholders})
            GROUP BY CustomerKey";

        for (int i = 0; i < keys.Count; i++)
        {
            DbParameter param = cmd.CreateParameter();
            param.ParameterName = "@k" + i;
            param.Value = keys[i];
            cmd.Parameters.Add(param);
        }

        using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int ck = reader.GetInt32(reader.GetOrdinal("CustomerKey"));
            int active = reader.IsDBNull(reader.GetOrdinal("ActiveTicketCount"))
                ? 0
                : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ActiveTicketCount")));
            string? lastIssue = reader.IsDBNull(reader.GetOrdinal("LastIssueDate"))
                ? null
                : reader.GetString(reader.GetOrdinal("LastIssueDate"));
            dict[ck] = (active, lastIssue);
        }

        return dict;
    }
}
