using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.HubClient;

namespace SmsOpsHQ.Api.Controllers;

/// <summary>
/// Operator endpoints for the store's HQ Hub client. Currently exposes only
/// <c>POST /api/hub/reload</c>, which the desktop Settings UI calls right after
/// saving <c>hub_config.json</c> so the new URL/StoreKey/DeploymentId take
/// effect without an app restart.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hub")]
public sealed class HubController : ControllerBase
{
    private readonly IHubSignalRClient _signalR;
    private readonly IHeartbeatPusher _pusher;
    private readonly ILogger<HubController> _logger;

    public HubController(
        IHubSignalRClient signalR,
        IHeartbeatPusher pusher,
        ILogger<HubController> logger)
    {
        _signalR = signalR;
        _pusher = pusher;
        _logger = logger;
    }

    /// <summary>
    /// Force the Hub client to re-read <c>%AppData%\SmsOpsHQ\hub_config.json</c>
    /// and rebuild its SignalR connection / heartbeat timer in-place. Returns
    /// the new effective state so the UI can immediately show whether the
    /// store is reporting to HQ.
    /// </summary>
    [HttpPost("reload")]
    public async Task<IActionResult> Reload(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Hub reload requested by {User}", User.Identity?.Name ?? "(anon)");

        try
        {
            await _signalR.ReloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub reload failed");
            return Problem(
                title: "Hub reload failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Give the (re-)connect attempt a short window to complete so the
        // returned isConnected flag reflects reality the operator will see.
        // Total wait is bounded by both attempt count and request cancellation.
        for (int i = 0; i < 20 && !_signalR.IsConnected && _pusher.IsConfigured; i++)
        {
            try { await Task.Delay(100, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }

        return Ok(new
        {
            enabled = _pusher.IsConfigured,
            isConnected = _signalR.IsConnected,
            hubUrl = _pusher.HubUrl,
            deploymentId = _pusher.DeploymentId,
            intervalSeconds = _pusher.IntervalSeconds
        });
    }
}
