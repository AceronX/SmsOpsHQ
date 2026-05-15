using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class PawnCalculatorTests
{
    // Fixed "today" for deterministic tests
    private static readonly DateTime Today = new DateTime(2026, 2, 6);

    // =================================================================
    // CalculateDaysLate
    // =================================================================

    [Fact]
    public void CalculateDaysLate_PastDue_ReturnsPositive()
    {
        DateTime maturity = new DateTime(2026, 1, 23); // 14 days ago
        int? result = PawnCalculator.CalculateDaysLate(maturity, Today);
        Assert.Equal(14, result);
    }

    [Fact]
    public void CalculateDaysLate_DueToday_ReturnsZero()
    {
        int? result = PawnCalculator.CalculateDaysLate(Today, Today);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateDaysLate_NotYetDue_ReturnsNegative()
    {
        DateTime maturity = new DateTime(2026, 3, 3); // 25 days from now
        int? result = PawnCalculator.CalculateDaysLate(maturity, Today);
        Assert.Equal(-25, result);
    }

    [Fact]
    public void CalculateDaysLate_NullMaturity_ReturnsNull()
    {
        int? result = PawnCalculator.CalculateDaysLate(null, Today);
        Assert.Null(result);
    }

    // =================================================================
    // CalculateCpuOverdueDays
    // =================================================================

    [Fact]
    public void CalculateCpuOverdueDays_ClosedLate_ReturnsPositive()
    {
        DateTime closed = new DateTime(2026, 1, 15);
        DateTime maturity = new DateTime(2026, 1, 1);
        int result = PawnCalculator.CalculateCpuOverdueDays(closed, maturity);
        Assert.Equal(14, result);
    }

    [Fact]
    public void CalculateCpuOverdueDays_ClosedOnDueDate_ReturnsZero()
    {
        DateTime date = new DateTime(2026, 1, 1);
        int result = PawnCalculator.CalculateCpuOverdueDays(date, date);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateCpuOverdueDays_ClosedEarly_ReturnsZero()
    {
        DateTime closed = new DateTime(2025, 12, 25);
        DateTime maturity = new DateTime(2026, 1, 1);
        int result = PawnCalculator.CalculateCpuOverdueDays(closed, maturity);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateCpuOverdueDays_NullClosed_ReturnsZero()
    {
        int result = PawnCalculator.CalculateCpuOverdueDays(null, new DateTime(2026, 1, 1));
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateCpuOverdueDays_NullMaturity_ReturnsZero()
    {
        int result = PawnCalculator.CalculateCpuOverdueDays(new DateTime(2026, 1, 15), null);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateCpuOverdueDays_BothNull_ReturnsZero()
    {
        int result = PawnCalculator.CalculateCpuOverdueDays(null, null);
        Assert.Equal(0, result);
    }

    // =================================================================
    // CalculateCpuScore
    // =================================================================

    [Fact]
    public void CalculateCpuScore_VariousTiers_ScoresCorrectly()
    {
        // 45d=+15, 25d=+10, 10d=+5, 3d=+0 => total 30
        List<int> overdueDays = new() { 45, 25, 10, 3 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);

        Assert.Equal(30, result.CpuScore);
        Assert.Equal(0, result.PfxPenalty);
        Assert.Equal(30, result.Score);
        Assert.False(result.SevereOverdue);
        Assert.Equal("Good", result.Level);
    }

    [Fact]
    public void CalculateCpuScore_WithPfxPenalty_ReducesScore()
    {
        // 30d=+15, 21d=+10 => cpuScore=25, pfx=2 => penalty=-10, total=15
        List<int> overdueDays = new() { 30, 21 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 2);

        Assert.Equal(25, result.CpuScore);
        Assert.Equal(-10, result.PfxPenalty);
        Assert.Equal(15, result.Score);
        Assert.Equal("Fair", result.Level);
    }

    [Fact]
    public void CalculateCpuScore_SevereOverdue_Applies40PercentReduction()
    {
        // 60d=severe(0), 30d=+15, 21d=+10 => cpuScore=25
        // pfx=0, pfxPenalty=0, total=25 * 0.6 = 15
        List<int> overdueDays = new() { 60, 30, 21 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);

        Assert.Equal(25, result.CpuScore);
        Assert.True(result.SevereOverdue);
        Assert.Equal(15, result.Score); // (int)(25 * 0.6) = 15
        Assert.Equal("Fair", result.Level);
    }

    [Fact]
    public void CalculateCpuScore_OnlyPfx_NegativeScore()
    {
        // No CPU tickets, 3 PFX => score = -15
        List<int> overdueDays = new();
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 3);

        Assert.Equal(0, result.CpuScore);
        Assert.Equal(-15, result.PfxPenalty);
        Assert.Equal(-15, result.Score);
        Assert.Equal("High Risk", result.Level);
        Assert.Equal("#EF4444", result.Color);
    }

    [Fact]
    public void CalculateCpuScore_EmptyNoTickets_Zero()
    {
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(new List<int>(), pfxCount: 0);

        Assert.Equal(0, result.Score);
        Assert.Equal("Poor", result.Level); // score=0, >=0 threshold
    }

    [Fact]
    public void CalculateCpuScore_ExcellentCustomer()
    {
        // Many CPU tickets with moderate lateness: 5 x 30d(+15) + 5 x 21d(+10)
        // cpuScore = 75 + 50 = 125, no pfx, no severe => score=125, Excellent
        List<int> overdueDays = new() { 30, 30, 30, 30, 30, 21, 21, 21, 21, 21 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);

        Assert.Equal(125, result.Score);
        Assert.Equal("Excellent", result.Level);
        Assert.Equal("#10B981", result.Color);
    }

    [Fact]
    public void CalculateCpuScore_SevereWithPfx_CombinedPenalties()
    {
        // 90d=severe, 30d=+15 => cpuScore=15
        // pfx=4 => penalty=-20, pre-severe total=-5, severe: (int)(-5 * 0.6) = -3
        List<int> overdueDays = new() { 90, 30 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 4);

        Assert.Equal(15, result.CpuScore);
        Assert.Equal(-20, result.PfxPenalty);
        Assert.True(result.SevereOverdue);
        Assert.Equal(-3, result.Score); // (int)(-5 * 0.6) = -3
        Assert.Equal("High Risk", result.Level);
    }

    // =================================================================
    // AssessRisk
    // =================================================================

    [Theory]
    [InlineData(51.0, 0, "High Risk")]
    [InlineData(10.0, 3, "High Risk")]
    [InlineData(80.0, 5, "High Risk")]
    public void AssessRisk_HighRisk(double lateRate, int pfxCount, string expected)
    {
        Assert.Equal(expected, PawnCalculator.AssessRisk(lateRate, pfxCount));
    }

    [Theory]
    [InlineData(26.0, 0, "Medium Risk")]
    [InlineData(50.0, 0, "Medium Risk")]  // exactly 50, not >50
    [InlineData(5.0, 2, "Medium Risk")]
    [InlineData(25.0, 0, "Medium Risk")] // >20 late rate
    [InlineData(0.0, 1, "Medium Risk")] // any PFX counts as medium+
    public void AssessRisk_MediumRisk(double lateRate, int pfxCount, string expected)
    {
        Assert.Equal(expected, PawnCalculator.AssessRisk(lateRate, pfxCount));
    }

    [Theory]
    [InlineData(1.0, 0, "Low Risk")]
    [InlineData(20.0, 0, "Low Risk")] // exactly 20, not >20
    public void AssessRisk_LowRisk(double lateRate, int pfxCount, string expected)
    {
        Assert.Equal(expected, PawnCalculator.AssessRisk(lateRate, pfxCount));
    }

    [Fact]
    public void AssessRisk_ZeroLateZeroPfx_IsLowRisk()
    {
        Assert.Equal("Low Risk", PawnCalculator.AssessRisk(0.0, 0));
    }

    // =================================================================
    // CalculateLatePaymentHistory
    // =================================================================

    [Fact]
    public void CalculateLatePaymentHistory_EmptyList_ReturnsNoHistory()
    {
        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(new List<Ticket>(), Today);

        Assert.Equal(0, result.TotalTickets);
        Assert.Equal(0, result.LatePayments);
        Assert.Equal(0, result.OnTimePayments);
        Assert.Equal(0, result.PfxCount);
        Assert.Equal("No History", result.RiskLevel);
        Assert.Equal(0.0, result.LatePaymentRate);
    }

    [Fact]
    public void CalculateLatePaymentHistory_AllOnTime_ReturnsExcellent()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/1/2026", closed: "12/28/2025", howClosed: "CPU"),
            MakeClosedTicket(due: "1/15/2026", closed: "1/15/2026", howClosed: "CPU"),
            MakeClosedTicket(due: "12/1/2025", closed: "11/30/2025", howClosed: "RDM"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(3, result.TotalTickets);
        Assert.Equal(0, result.LatePayments);
        Assert.Equal(3, result.OnTimePayments);
        Assert.Equal(0.0, result.LatePaymentRate);
        Assert.Equal(100.0, result.OnTimeRate);
        Assert.Equal("Excellent", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_AllLate_ReturnsHighRisk()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/1/2026", closed: "1/20/2026", howClosed: "CPU"),
            MakeClosedTicket(due: "12/1/2025", closed: "12/30/2025", howClosed: "RDM"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(2, result.TotalTickets);
        Assert.Equal(2, result.LatePayments);
        Assert.Equal(0, result.OnTimePayments);
        Assert.Equal(100.0, result.LatePaymentRate);
        Assert.Equal("High Risk", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_PfxOnly_MediumRiskFromPfx()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/1/2026", closed: "2/1/2026", howClosed: "PFX-"),
            MakeClosedTicket(due: "12/1/2025", closed: "1/1/2026", howClosed: "PFX-"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(2, result.TotalTickets);
        Assert.Equal(2, result.PfxCount);
        Assert.Equal(0, result.LatePayments);
        Assert.Equal(0, result.OnTimePayments);
        // No total_with_due_date (all PFX), but pfx >= 1 triggers "Medium Risk" via fallback
        Assert.Equal("Medium Risk", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_ActiveLateTicket_CountedAsLate()
    {
        List<Ticket> tickets = new()
        {
            MakeActiveTicket(due: "1/1/2026"),  // Active, 36 days late
            MakeClosedTicket(due: "12/1/2025", closed: "12/1/2025", howClosed: "CPU"),  // On time
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(1, result.StillActive);
        Assert.Equal(1, result.LatePayments);
        Assert.Equal(1, result.OnTimePayments);
        Assert.Equal(50.0, result.LatePaymentRate);
        Assert.True(result.LateTicketsSample.Count > 0);
        Assert.Equal("ACTIVE (LATE)", result.LateTicketsSample[0].Status);
    }

    [Fact]
    public void CalculateLatePaymentHistory_ActiveNotDue_NotCountedAsLate()
    {
        List<Ticket> tickets = new()
        {
            MakeActiveTicket(due: "3/1/2026"),  // Active, not yet due
            MakeClosedTicket(due: "12/1/2025", closed: "12/1/2025", howClosed: "CPU"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(1, result.StillActive);
        Assert.Equal(0, result.LatePayments);
        Assert.Equal(1, result.OnTimePayments);
        Assert.Equal(0.0, result.LatePaymentRate);
    }

    [Fact]
    public void CalculateLatePaymentHistory_NoDueDates_CountedAsSuch()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: null, closed: "1/1/2026", howClosed: "CPU"),
            MakeClosedTicket(due: null, closed: "12/1/2025", howClosed: "RDM"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(2, result.NoDueDate);
        Assert.Equal("No History", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_MixedTickets_CorrectCounts()
    {
        List<Ticket> tickets = new()
        {
            // PFX
            MakeClosedTicket(due: "1/1/2026", closed: "2/1/2026", howClosed: "PFX-"),
            // Closed late (20 days)
            MakeClosedTicket(due: "1/1/2026", closed: "1/21/2026", howClosed: "CPU"),
            // Closed on time
            MakeClosedTicket(due: "12/15/2025", closed: "12/15/2025", howClosed: "CPU"),
            MakeClosedTicket(due: "12/1/2025", closed: "11/30/2025", howClosed: "RDM"),
            // Active late
            MakeActiveTicket(due: "1/20/2026"), // 17 days late
            // Active not due
            MakeActiveTicket(due: "6/1/2026"),
            // No due date
            MakeClosedTicket(due: null, closed: "1/1/2026", howClosed: "CPU"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(7, result.TotalTickets);
        Assert.Equal(1, result.PfxCount);
        Assert.Equal(2, result.LatePayments);    // 1 closed late + 1 active late
        Assert.Equal(2, result.OnTimePayments);   // 2 closed on time
        Assert.Equal(2, result.StillActive);      // 2 active tickets
        Assert.Equal(1, result.NoDueDate);
        Assert.Equal(50.0, result.LatePaymentRate); // 2 / (2+2) * 100
        Assert.Equal(50.0, result.OnTimeRate);
        // 50% > 25% and pfx=1 => Medium Risk. But 50% is not >50, so check exact.
        // lateRate=50.0 which is NOT > 50 (it's ==50), so High Risk doesn't trigger from rate.
        // pfxCount=1, which is < 3 and < 2, so just Low Risk from pfx path.
        // Actually: >25 OR >=2? lateRate=50>25 => Medium Risk.
        // Wait: 50>50? No. 50>25? Yes. So Medium Risk.
        Assert.Equal("Medium Risk", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_LateTicketsSortedByWorstFirst()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/30/2026", closed: "2/2/2026", howClosed: "CPU"),  // 3 days late
            MakeClosedTicket(due: "1/1/2026", closed: "1/31/2026", howClosed: "CPU"),  // 30 days late
            MakeClosedTicket(due: "1/15/2026", closed: "1/25/2026", howClosed: "CPU"), // 10 days late
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(3, result.LateTicketsSample.Count);
        Assert.Equal(30, result.LateTicketsSample[0].DaysLate);
        Assert.Equal(10, result.LateTicketsSample[1].DaysLate);
        Assert.Equal(3, result.LateTicketsSample[2].DaysLate);
    }

    [Fact]
    public void CalculateLatePaymentHistory_PfxSampleCappedAt3()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/1/2026", closed: "2/1/2026", howClosed: "PFX-"),
            MakeClosedTicket(due: "12/1/2025", closed: "1/1/2026", howClosed: "PFX-"),
            MakeClosedTicket(due: "11/1/2025", closed: "12/1/2025", howClosed: "PFX-"),
            MakeClosedTicket(due: "10/1/2025", closed: "11/1/2025", howClosed: "PFX-"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(4, result.PfxCount);
        Assert.Equal(3, result.PfxTicketsSample.Count); // Capped at 3
    }

    [Fact]
    public void CalculateLatePaymentHistory_ThreePlusPfxNoOtherTickets_HighRisk()
    {
        List<Ticket> tickets = new()
        {
            MakeClosedTicket(due: "1/1/2026", closed: "2/1/2026", howClosed: "PFX-"),
            MakeClosedTicket(due: "12/1/2025", closed: "1/1/2026", howClosed: "PFX-"),
            MakeClosedTicket(due: "11/1/2025", closed: "12/1/2025", howClosed: "PFX-"),
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(3, result.PfxCount);
        Assert.Equal("High Risk", result.RiskLevel);
    }

    [Fact]
    public void CalculateLatePaymentHistory_ClosedNoDateClosed_CountsUnknownNotOnTime()
    {
        List<Ticket> tickets = new()
        {
            new Ticket
            {
                Key = 1,
                CustomerKey = 100,
                TransNo = 1001,
                Active = 0,
                DueDate = "1/1/2026",
                DateClosed = null,
                HowClosed = "CPU"
            }
        };

        LatePaymentHistory result = PawnCalculator.CalculateLatePaymentHistory(tickets, Today);

        Assert.Equal(0, result.OnTimePayments);
        Assert.Equal(0, result.LatePayments);
        Assert.Equal(1, result.UnknownClosedDateCount);
    }

    // =================================================================
    // Edge cases
    // =================================================================

    [Fact]
    public void CalculateCpuScore_AllOnTime_ZeroScore()
    {
        // All tickets closed within 0-6 days: no points
        List<int> overdueDays = new() { 0, 1, 2, 3, 4, 5, 6 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);

        Assert.Equal(0, result.CpuScore);
        Assert.Equal(0, result.Score);
        Assert.Equal("Poor", result.Level);
        Assert.False(result.SevereOverdue);
    }

    [Fact]
    public void CalculateCpuScore_BoundaryAt7Days()
    {
        List<int> overdueDays = new() { 7 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);
        Assert.Equal(5, result.Score);
    }

    [Fact]
    public void CalculateCpuScore_BoundaryAt21Days()
    {
        List<int> overdueDays = new() { 21 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);
        Assert.Equal(10, result.Score);
    }

    [Fact]
    public void CalculateCpuScore_BoundaryAt30Days()
    {
        List<int> overdueDays = new() { 30 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);
        Assert.Equal(15, result.Score);
    }

    [Fact]
    public void CalculateCpuScore_BoundaryAt59Days()
    {
        // 59 is in the 30-59 range, should score +15
        List<int> overdueDays = new() { 59 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);
        Assert.Equal(15, result.Score);
        Assert.False(result.SevereOverdue);
    }

    [Fact]
    public void CalculateCpuScore_BoundaryAt60Days()
    {
        // 60 is severe, no points for the ticket
        List<int> overdueDays = new() { 60 };
        CpuScoreResult result = PawnCalculator.CalculateCpuScore(overdueDays, pfxCount: 0);
        Assert.Equal(0, result.CpuScore);
        Assert.True(result.SevereOverdue);
        Assert.Equal(0, result.Score); // (int)(0 * 0.6) = 0
    }

    [Fact]
    public void CalculateDaysLate_TimeComponentIgnored()
    {
        // Even with time components, should compare dates only
        DateTime maturity = new DateTime(2026, 2, 5, 23, 59, 59);
        DateTime today = new DateTime(2026, 2, 6, 0, 0, 1);
        int? result = PawnCalculator.CalculateDaysLate(maturity, today);
        Assert.Equal(1, result);
    }

    // =================================================================
    // Helpers
    // =================================================================

    private static Ticket MakeClosedTicket(
        string? due,
        string? closed,
        string howClosed,
        int transNo = 1000,
        double amount = 100000)
    {
        return new Ticket
        {
            Key = transNo,
            CustomerKey = 100,
            TransNo = transNo,
            Active = 0,
            DueDate = due,
            DateClosed = closed,
            HowClosed = howClosed,
            Amount = amount
        };
    }

    private static Ticket MakeActiveTicket(
        string? due,
        int transNo = 2000)
    {
        return new Ticket
        {
            Key = transNo,
            CustomerKey = 100,
            TransNo = transNo,
            Active = 1,
            DueDate = due,
            DateClosed = null,
            HowClosed = null
        };
    }
}
