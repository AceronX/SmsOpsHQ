using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Services;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/review-automation")]
public sealed class ReviewAutomationController : ControllerBase
{
    private readonly ReviewAutomationSettingsStore _settingsStore;
    private readonly IReviewAutomationScheduler _scheduler;
    private readonly ILogger<ReviewAutomationController> _logger;

    public ReviewAutomationController(
        ReviewAutomationSettingsStore settingsStore,
        IReviewAutomationScheduler scheduler,
        ILogger<ReviewAutomationController> logger)
    {
        _settingsStore = settingsStore;
        _scheduler = scheduler;
        _logger = logger;
    }

    [HttpGet("settings")]
    public ActionResult<ReviewAutomationSettings> GetSettings()
    {
        return Ok(_settingsStore.Load());
    }

    [HttpPut("settings")]
    public IActionResult PutSettings([FromBody] ReviewAutomationSettings body)
    {
        if (body.IntervalMinutes < 1 || body.IntervalMinutes > 24 * 60)
            return BadRequest(new { message = "IntervalMinutes must be between 1 and 1440." });

        _settingsStore.Save(body);
        _scheduler.ApplySettingsFromStore();
        _logger.LogInformation("Review automation settings updated via API.");
        return Ok(_settingsStore.Load());
    }

    [HttpGet("status")]
    public ActionResult<ReviewAutomationSchedulerStatus> GetStatus()
    {
        return Ok(_scheduler.GetStatus());
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunNow(CancellationToken cancellationToken)
    {
        ReviewAutomationResult result = await _scheduler.RunNowAsync(cancellationToken);
        if (result.Detail == "run_already_in_progress")
            return Conflict(new { message = "A review automation run is already in progress.", result });

        return Ok(result);
    }
}
