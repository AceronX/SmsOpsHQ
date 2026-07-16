using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.HubClient;

namespace SmsOpsHQ.Api.Controllers;

/// <summary>
/// Operator endpoints for the store's HQ Hub client.
///   * <c>GET /api/hub/status</c>    - returns the effective, secret-free Hub
///     configuration and live connection diagnostics.
///   * <c>POST /api/hub/reload</c>   - desktop Settings UI calls this after
///     saving <c>hub_config.json</c> so the new URL/StoreKey/DeploymentId take
///     effect without an app restart.
///   * <c>POST /api/hub/shutdown</c> - the desktop app's <c>OnExit</c> calls
///     this right before it kills the bundled API process, so the SignalR
///     "goodbye" reaches the Hub and the dashboard flips to offline within
///     ~1 second instead of waiting for the SignalR keepalive timeout.
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
    /// Return the effective Hub settings and current connection diagnostics.
    /// The Store Key is intentionally never included.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(BuildStatus());
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

        return Ok(BuildStatus());
    }

    /// <summary>
    /// Gracefully close the SignalR connection to HQ (sends the SignalR
    /// "goodbye" frame and waits up to ~2s for the server to ack). The
    /// desktop app calls this synchronously from <c>App.OnExit</c>, BEFORE it
    /// hard-kills the bundled API process, so the Hub's
    /// <c>OnDisconnectedAsync</c> fires immediately and the dashboard flips
    /// to offline within ~1 second.
    ///
    /// Idempotent: safe to call when the client is already stopped, never
    /// connected, or Hub reporting is disabled. Never returns 5xx for those
    /// cases -- they're a normal "no-op" outcome.
    /// </summary>
    [HttpPost("shutdown")]
    public async Task<IActionResult> Shutdown(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Hub shutdown requested by {User}", User.Identity?.Name ?? "(anon)");

        // 2s is enough for a WebSocket close handshake on any sane network.
        // The request itself can be cancelled by the client closing early.
        try
        {
            await _signalR.StopAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (Exception ex)
        {
            // StopAsync swallows its own failures, but be defensive: even if
            // graceful stop blew up, we want the response to be a clean 200 so
            // the desktop shutdown path doesn't pop a dialog.
            _logger.LogWarning(ex, "Hub graceful shutdown threw; the API will exit anyway");
        }

        return Ok(new { stopped = !_signalR.IsConnected });
    }

    private HubStatusResponse BuildStatus()
    {
        HeartbeatPusherStatus status = _pusher.GetStatus();
        return new HubStatusResponse
        {
            Enabled = status.Enabled,
            Configured = _pusher.IsConfigured,
            IsConnected = _signalR.IsConnected,
            HubUrl = status.HubUrl,
            DeploymentId = status.DeploymentId,
            IntervalSeconds = status.IntervalSeconds,
            LastAttemptUtc = status.LastAttemptUtc,
            LastSuccessUtc = status.LastSuccessUtc,
            LastError = status.LastError,
            SuccessCount = status.SuccessCount,
            FailureCount = status.FailureCount
        };
    }
}

public sealed class HubStatusResponse
{
    public bool Enabled { get; init; }
    public bool Configured { get; init; }
    public bool IsConnected { get; init; }
    public string HubUrl { get; init; } = string.Empty;
    public string DeploymentId { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; }
    public DateTime? LastAttemptUtc { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public string? LastError { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}
