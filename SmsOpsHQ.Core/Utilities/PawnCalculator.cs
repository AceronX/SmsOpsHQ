using System.Globalization;
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

// Pawn ticket business logic: days-late, CPU scoring, late payment
// history, customer quality, and risk assessment.
// Ported from Python routes_customers.py and PAWN_LOGIC.md.
public static class PawnCalculator
{
    private static readonly string[] XpdDateFormats = { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };

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
        List<LateTicketInfo> lateTickets = new();
        List<PfxTicketInfo> pfxTickets = new();

        foreach (Ticket ticket in tickets)
        {
            // PFX (forfeited) tickets are counted separately — exact match on "PFX-"
            if (ticket.HowClosed == "PFX-")
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

            // Skip tickets with no due date
            if (!TryParseXpdDate(ticket.DueDate, out DateTime dueDt))
            {
                noDueDate++;
                continue;
            }

            // Active tickets
            if (ticket.Active == 1)
            {
                stillActive++;
                if (dueDt < today.Date)
                {
                    // Currently late
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

            // Closed tickets: compare close date to due date
            if (TryParseXpdDate(ticket.DateClosed, out DateTime closedDt))
            {
                if (closedDt > dueDt)
                {
                    // Closed late
                    int daysLate = (closedDt - dueDt).Days;
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
            else
            {
                // No close date but not active -- assume on time
                onTimeCount++;
            }
        }

        // Calculate rates
        int totalWithDueDate = lateCount + onTimeCount;
        double lateRate = 0.0;
        string riskLevel;

        if (totalWithDueDate > 0)
        {
            lateRate = (double)lateCount / totalWithDueDate * 100.0;
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
            riskLevel = "Low Risk";
        }

        // Sort late tickets by worst first
        lateTickets.Sort((LateTicketInfo a, LateTicketInfo b) => b.DaysLate.CompareTo(a.DaysLate));

        return new LatePaymentHistory
        {
            TotalTickets = tickets.Count,
            LatePayments = lateCount,
            OnTimePayments = onTimeCount,
            PfxCount = pfxCount,
            StillActive = stillActive,
            NoDueDate = noDueDate,
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

    // Try to parse an XPD date string ("M/d/yyyy" or "M/d/yyyy h:mm:ss tt").
    // Splits on space and parses the date portion.
    private static bool TryParseXpdDate(string? dateString, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(dateString))
            return false;

        string datePart = dateString.Contains(' ')
            ? dateString.Split(' ')[0]
            : dateString;

        return DateTime.TryParseExact(
            datePart,
            XpdDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }
}
