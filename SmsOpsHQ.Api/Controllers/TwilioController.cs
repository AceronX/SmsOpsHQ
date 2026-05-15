using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

// Diagnostics for the Twilio integration. Lets the desktop app (and any operator)
// see at a glance whether the API is configured to actually send SMS, or whether
// it is silently in "mock" mode (no AccountSid/AuthToken — outbound messages do
// NOT reach the carrier).
[ApiController]
[Authorize]
[Route("api/twilio")]
public sealed class TwilioController : ControllerBase
{
    private readonly ITwilioService _twilioService;

    public TwilioController(ITwilioService twilioService)
    {
        _twilioService = twilioService;
    }

    // GET /api/twilio/status
    // Safe to call from the desktop client — does NOT return any secret values,
    // only flags + a 6-char SID prefix for visual confirmation.
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        bool mock = _twilioService.IsMockMode;
        return Ok(new
        {
            mode = mock ? "mock" : "live",
            mock,
            account_sid_prefix = _twilioService.AccountSidPrefix,
            has_messaging_service = _twilioService.HasMessagingService,
            warning = mock
                ? "Twilio is in MOCK mode. Outbound SMS will NOT be delivered to customers. " +
                  "Configure AccountSid and AuthToken in the desktop app under Settings → Twilio, " +
                  "then restart the API (or wait for the next request — credentials are reloaded automatically)."
                : null
        });
    }
}
