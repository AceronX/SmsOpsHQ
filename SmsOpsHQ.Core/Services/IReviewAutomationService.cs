namespace SmsOpsHQ.Core.Services;

// Periodically processes new XPD ticket rows and sends one review SMS per customer (URL rotation in ReviewService).
public interface IReviewAutomationService
{
    Task<ReviewAutomationResult> ProcessNewTicketsAsync(CancellationToken cancellationToken = default);
}

public sealed class ReviewAutomationResult
{
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public string? Detail { get; set; }
}
