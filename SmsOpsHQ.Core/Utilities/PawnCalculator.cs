using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Utilities;

// Result of CalculateLatePaymentHistory.
public sealed class LatePaymentHistory
{
    public int TotalTickets { get; set; }
    public int LatePayments { get; set; }
    public int OnTimePayments { get; set; }
    public int PfxCount { get; set; }
    public int StillActive { get; set; }
    public int NoDueDate { get; set; }
    public int UnknownClosedDateCount { get; set; }
    public double LatePaymentRate { get; set; }
    public double OnTimeRate { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public List<LateTicketInfo> LateTicketsSample { get; set; } = new();
    public List<PfxTicketInfo> PfxTicketsSample { get; set; } = new();
}

// Info about a single late ticket, for display in the late-tickets sample.
public sealed class LateTicketInfo
{
    public int? TransNo { get; set; }
    public string? DueDate { get; set; }
    public string? ClosedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DaysLate { get; set; }
}

// Info about a single PFX (forfeited) ticket, for display in the PFX sample.
public sealed class PfxTicketInfo
{
    public int? TransNo { get; set; }
    public string? IssueDate { get; set; }
    public string? ClosedDate { get; set; }
    public double Amount { get; set; }
}

// Result of CalculateCpuScore.
public sealed class CpuScoreResult
{
    public int Score { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int CpuScore { get; set; }
    public int PfxPenalty { get; set; }
    public bool SevereOverdue { get; set; }
}

// Lightweight input for profile-level flags (built by the controller from customer data).
public sealed class ProfileFlags
{
    public bool HasID { get; set; }
    public bool HasAddress { get; set; }
    public bool HasContact { get; set; }
}

// Result of CalculateDecisionCard — unified scoring across history, profile, and risk.
public sealed class DecisionCardResult
{
    public int CustomerScore { get; set; }
    public string ScoreBand { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string PrimaryReason { get; set; } = string.Empty;
    public string ReviewReasons { get; set; } = string.Empty;
    public int ActiveTickets { get; set; }
    public int OverdueActiveTickets { get; set; }
    public int AllTimeTickets { get; set; }
    public int CpuCount { get; set; }
    public int PfxCount { get; set; }
    public bool EverLate { get; set; }
    public double AvgDaysLate { get; set; }
    public int LateRedeemedCount { get; set; }
    public int OnTimeRedeemedCount { get; set; }
    public double? LatePaymentRate { get; set; }
    public bool FlagMissingID { get; set; }
    public bool FlagMissingAddress { get; set; }
    public bool FlagMissingContact { get; set; }
}

// Pawn ticket business logic: days-late, CPU scoring, late payment
// history, customer quality, and risk assessment.
// Ported from Python routes_customers.py and PAWN_LOGIC.md.
public static class PawnCalculator
{
    public static bool IsPfxHowClosed(string? howClosed) =>
        string.Equals((howClosed ?? "").Trim(), "PFX-", StringComparison.OrdinalIgnoreCase);

    public static bool IsCpuHowClosed(string? howClosed) =>
        string.Equals((howClosed ?? "").Trim(), "CPU", StringComparison.OrdinalIgnoreCase);

    // Calculate how many days late a ticket is relative to today.
    // Positive = past due, Zero = due today, Negative = not yet due.
    // Returns null if maturityDate is null.
    public static int? CalculateDaysLate(DateTime? maturityDate, DateTime today)
    {
        if (maturityDate is null)
            return null;

        return (today.Date - maturityDate.Value.Date).Days;
    }

    // Calculate how many days overdue a CPU (Closed Paid Up) ticket was
    // at the time it was closed. Returns 0 if closed on time or early,
    // or if either date is null.
    public static int CalculateCpuOverdueDays(DateTime? dateClosed, DateTime? maturityDate)
    {
        if (dateClosed is null || maturityDate is null)
            return 0;

        int overdueDays = (dateClosed.Value.Date - maturityDate.Value.Date).Days;
        return overdueDays > 0 ? overdueDays : 0;
    }

    // Analyze a customer's full ticket history for late payments, PFX
    // forfeitures, and risk assessment. Tickets are categorized as:
    //   PFX -> pfxCount only
    //   No due date -> noDueDate only
    //   Active + late -> lateCount + stillActive
    //   Active + not due -> stillActive only
    //   Closed late -> lateCount
    //   Closed on-time -> onTimeCount
    public static LatePaymentHistory CalculateLatePaymentHistory(List<Ticket> tickets, DateTime today)
    {
        int lateCount = 0;
        int onTimeCount = 0;
        int noDueDate = 0;
        int stillActive = 0;
        int pfxCount = 0;
        int unknownClosedDate = 0;
        List<LateTicketInfo> lateTickets = new();
        List<PfxTicketInfo> pfxTickets = new();

        foreach (Ticket ticket in tickets)
        {
            if (IsPfxHowClosed(ticket.HowClosed))
            {
                pfxCount++;
                pfxTickets.Add(new PfxTicketInfo
                {
                    TransNo = ticket.TransNo,
                    IssueDate = ticket.IssueDate,
                    ClosedDate = ticket.DateClosed,
                    Amount = ticket.Amount.HasValue ? ticket.Amount.Value / 1000.0 : 0.0
                });
                continue;
            }

            bool isPawnActive = ticket.Active == 1 && ticket.Type != 0;
            if (isPawnActive)
            {
                stillActive++;
                if (!XpdDateParser.TryParse(ticket.DueDate, out DateTime dueDt))
                {
                    noDueDate++;
                    continue;
                }

                if (dueDt < today.Date)
                {
                    int daysLate = (today.Date - dueDt).Days;
                    lateCount++;
                    lateTickets.Add(new LateTicketInfo
                    {
                        TransNo = ticket.TransNo,
                        DueDate = ticket.DueDate,
                        Status = "ACTIVE (LATE)",
                        DaysLate = daysLate
                    });
                }

                continue;
            }

            if (ticket.Active == 1)
                continue;

            if (!XpdDateParser.TryParse(ticket.DueDate, out DateTime dueClosed))
            {
                noDueDate++;
                continue;
            }

            if (!XpdDateParser.TryParse(ticket.DateClosed, out DateTime closedDt))
            {
                unknownClosedDate++;
                continue;
            }

            if (closedDt > dueClosed)
            {
                int daysLate = (closedDt - dueClosed).Days;
                lateCount++;
                lateTickets.Add(new LateTicketInfo
                {
                    TransNo = ticket.TransNo,
                    DueDate = ticket.DueDate,
                    ClosedDate = ticket.DateClosed,
                    Status = $"CLOSED LATE ({ticket.HowClosed})",
                    DaysLate = daysLate
                });
            }
            else
            {
                onTimeCount++;
            }
        }

        int totalWithDueDate = lateCount + onTimeCount;
        double lateRate = 0.0;
        string riskLevel;

        if (tickets.Count == 0)
        {
            riskLevel = "No History";
        }
        else if (totalWithDueDate > 0)
        {
            lateRate = (double)lateCount / totalWithDueDate * 100.0;
            if (lateRate == 0 && pfxCount == 0)
                riskLevel = "Excellent";
            else
                riskLevel = AssessRisk(lateRate, pfxCount);
        }
        else if (pfxCount >= 3)
        {
            riskLevel = "High Risk";
        }
        else if (pfxCount >= 1)
        {
            riskLevel = "Medium Risk";
        }
        else
        {
            riskLevel = "No History";
        }

        lateTickets.Sort((LateTicketInfo a, LateTicketInfo b) => b.DaysLate.CompareTo(a.DaysLate));

        return new LatePaymentHistory
        {
            TotalTickets = tickets.Count,
            LatePayments = lateCount,
            OnTimePayments = onTimeCount,
            PfxCount = pfxCount,
            StillActive = stillActive,
            NoDueDate = noDueDate,
            UnknownClosedDateCount = unknownClosedDate,
            LatePaymentRate = Math.Round(lateRate, 1),
            OnTimeRate = totalWithDueDate > 0 ? Math.Round(100.0 - lateRate, 1) : 0.0,
            RiskLevel = riskLevel,
            LateTicketsSample = lateTickets.Take(5).ToList(),
            PfxTicketsSample = pfxTickets.Take(3).ToList()
        };
    }

    // Calculate customer quality score from CPU ticket overdue days and PFX count.
    //   60+ days overdue: severe flag (no points for that ticket)
    //   30-59 days: +15 points
    //   21-29 days: +10 points
    //   7-20 days: +5 points
    //   0-6 days: +0 points
    //   PFX penalty: pfxCount * -5
    //   Severe penalty: totalScore * 0.6
    public static CpuScoreResult CalculateCpuScore(List<int> cpuOverdueDays, int pfxCount)
    {
        int cpuScore = 0;
        bool hasSevereOverdue = false;

        foreach (int overdueDays in cpuOverdueDays)
        {
            if (overdueDays >= 60)
            {
                hasSevereOverdue = true;
            }
            else if (overdueDays >= 30)
            {
                cpuScore += 15;
            }
            else if (overdueDays >= 21)
            {
                cpuScore += 10;
            }
            else if (overdueDays >= 7)
            {
                cpuScore += 5;
            }
            // 0-6 days: +0
        }

        int pfxPenalty = pfxCount * -5;
        int totalScore = cpuScore + pfxPenalty;

        // Apply 40% reduction for severe overdue
        if (hasSevereOverdue)
        {
            totalScore = (int)(totalScore * 0.6);
        }

        // Determine quality level
        string level;
        string color;

        if (totalScore >= 50)
        {
            level = "Excellent";
            color = "#10B981"; // green
        }
        else if (totalScore >= 30)
        {
            level = "Good";
            color = "#3B82F6"; // blue
        }
        else if (totalScore >= 10)
        {
            level = "Fair";
            color = "#F59E0B"; // yellow
        }
        else if (totalScore >= 0)
        {
            level = "Poor";
            color = "#F97316"; // orange
        }
        else
        {
            level = "High Risk";
            color = "#EF4444"; // red
        }

        return new CpuScoreResult
        {
            Score = totalScore,
            Level = level,
            Color = color,
            CpuScore = cpuScore,
            PfxPenalty = pfxPenalty,
            SevereOverdue = hasSevereOverdue
        };
    }

    // Unified decision card: consolidates ticket history, profile checks, and scoring
    // into a single result with a 0-100 score, band, action, and review reasons.
    public static DecisionCardResult CalculateDecisionCard(
        List<Ticket> tickets, ProfileFlags profileFlags, DateTime today)
    {
        int activeCount = 0;
        int overdueActiveCount = 0;
        int pfxCount = 0;
        int cpuCount = 0;
        int lateRedeemedCount = 0;
        int onTimeRedeemedCount = 0;
        double totalDaysLate = 0;
        int daysLateEntries = 0;

        foreach (Ticket t in tickets)
        {
            if (IsPfxHowClosed(t.HowClosed))
            {
                pfxCount++;
                continue;
            }

            if (t.Active == 1 && t.Type != 0)
            {
                activeCount++;
                if (XpdDateParser.TryParse(t.DueDate, out DateTime dueDt) && dueDt < today.Date)
                {
                    overdueActiveCount++;
                    totalDaysLate += (today.Date - dueDt).Days;
                    daysLateEntries++;
                }

                continue;
            }

            if (t.Active == 1)
                continue;

            if (IsCpuHowClosed(t.HowClosed))
                cpuCount++;

            if (!XpdDateParser.TryParse(t.DueDate, out DateTime closedDueDt))
                continue;

            if (!XpdDateParser.TryParse(t.DateClosed, out DateTime closedDt))
                continue;

            if (closedDt > closedDueDt)
            {
                lateRedeemedCount++;
                totalDaysLate += (closedDt - closedDueDt).Days;
                daysLateEntries++;
            }
            else
            {
                onTimeRedeemedCount++;
            }
        }

        int completedTickets = lateRedeemedCount + onTimeRedeemedCount;
        bool everLate = lateRedeemedCount > 0 || overdueActiveCount > 0;
        double avgDaysLate = daysLateEntries > 0 ? totalDaysLate / daysLateEntries : 0;
        double? latePaymentRate = completedTickets > 0
            ? (double)lateRedeemedCount / completedTickets * 100.0
            : null;

        // Scoring: start at 100 and subtract penalties.
        int score = 100;
        var reasons = new List<(string tag, int penalty)>();

        if (pfxCount > 0)
        {
            int penalty = Math.Min(pfxCount * 10, 40);
            score -= penalty;
            reasons.Add(($"{pfxCount} PFX", penalty));
        }

        if (overdueActiveCount > 0)
        {
            int penalty = Math.Min(overdueActiveCount * 10, 30);
            score -= penalty;
            reasons.Add(("OVERDUE ACTIVE", penalty));
        }

        if (latePaymentRate.HasValue)
        {
            if (latePaymentRate.Value > 50)
            {
                score -= 15;
                reasons.Add(("HIGH LATE RATE", 15));
            }
            else if (latePaymentRate.Value > 20)
            {
                score -= 8;
                reasons.Add(("ELEVATED LATE RATE", 8));
            }
        }

        if (avgDaysLate > 30)
        {
            score -= 10;
            reasons.Add(("HIGH AVG DAYS LATE", 10));
        }
        else if (avgDaysLate > 7)
        {
            score -= 5;
            reasons.Add(("ELEVATED AVG DAYS LATE", 5));
        }

        if (completedTickets == 0)
        {
            score -= 5;
            reasons.Add(("FIRST-TIME CUSTOMER", 5));
        }

        var reviewFlags = new List<string>();
        foreach (var (tag, _) in reasons)
            reviewFlags.Add(tag);

        if (!profileFlags.HasID) reviewFlags.Add("NO ID");
        if (!profileFlags.HasAddress) reviewFlags.Add("NO ADDRESS");
        if (!profileFlags.HasContact) reviewFlags.Add("NO CONTACT");

        score = Math.Max(0, score);

        string band;
        string action;
        if (score >= 80)
        {
            band = "STANDARD";
            action = "No action needed";
        }
        else if (score >= 60)
        {
            band = "VERIFY";
            action = "Verify photo ID and current address";
        }
        else if (score >= 40)
        {
            band = "VERIFY + MANAGER";
            action = "Verify ID and address; request manager review";
        }
        else
        {
            band = "MANAGER ONLY";
            action = "Manager approval required before proceeding";
        }

        string primaryReason = reasons.Count > 0
            ? reasons.OrderByDescending(r => r.penalty).First().tag
            : (reviewFlags.Count > 0 ? reviewFlags[0] : "No issues found");

        primaryReason = primaryReason switch
        {
            var r when r.Contains("PFX") => "Prior forfeiture history",
            "OVERDUE ACTIVE" => "Multiple overdue active tickets",
            "HIGH LATE RATE" => "High late payment rate",
            "ELEVATED LATE RATE" => "Elevated late payment rate",
            "HIGH AVG DAYS LATE" => "High average days late",
            "ELEVATED AVG DAYS LATE" => "Elevated average days late",
            "FIRST-TIME CUSTOMER" => "First-time customer — no history",
            "NO ID" => "Missing ID on file",
            "NO ADDRESS" => "Missing address on file",
            "NO CONTACT" => "Missing contact information",
            _ => primaryReason
        };

        return new DecisionCardResult
        {
            CustomerScore = score,
            ScoreBand = band,
            RecommendedAction = action,
            PrimaryReason = primaryReason,
            ReviewReasons = string.Join("; ", reviewFlags),
            ActiveTickets = activeCount,
            OverdueActiveTickets = overdueActiveCount,
            AllTimeTickets = tickets.Count,
            CpuCount = cpuCount,
            PfxCount = pfxCount,
            EverLate = everLate,
            AvgDaysLate = Math.Round(avgDaysLate, 1),
            LateRedeemedCount = lateRedeemedCount,
            OnTimeRedeemedCount = onTimeRedeemedCount,
            LatePaymentRate = latePaymentRate.HasValue ? Math.Round(latePaymentRate.Value, 1) : null,
            FlagMissingID = !profileFlags.HasID,
            FlagMissingAddress = !profileFlags.HasAddress,
            FlagMissingContact = !profileFlags.HasContact
        };
    }

    // Assess risk level from late payment rate and PFX count.
    // Matches Python: > 50 or pfx >= 3 → High, > 20 or pfx >= 1 → Medium, else Low.
    public static string AssessRisk(double latePaymentRate, int pfxCount)
    {
        if (latePaymentRate > 50.0 || pfxCount >= 3)
            return "High Risk";

        if (latePaymentRate > 20.0 || pfxCount >= 1)
            return "Medium Risk";

        return "Low Risk";
    }
}
