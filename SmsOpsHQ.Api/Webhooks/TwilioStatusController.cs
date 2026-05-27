using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Webhooks;

// Handles Twilio delivery status callbacks sent directly to this store (legacy
// /per-store webhook path or local dev). No [Authorize] -- Twilio sends these
// unauthenticated. Route: POST /twilio-sms/status
//
// The pipeline lives in IMessageStatusProcessor so the central Hub SignalR
// receiver can run the exact same logic (see Phase 5).
[ApiController]
[Route("twilio-sms")]
public sealed class TwilioStatusController : ControllerBase
{
    private readonly IMessageStatusProcessor _processor;
    private readonly ILogger<TwilioStatusController> _logger;

    public TwilioStatusController(
        IMessageStatusProcessor processor,
        ILogger<TwilioStatusController> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [HttpPost("status")]
    public async Task<IActionResult> HandleStatus(CancellationToken cancellationToken)
    {
        IFormCollection form = await Request.ReadFormAsync(cancellationToken);

        MessageStatusUpdate update = new()
        {
            MessageSid = form["MessageSid"].ToString(),
            MessageStatus = form["MessageStatus"].ToString(),
            ErrorCode = form.ContainsKey("ErrorCode") ? form["ErrorCode"].ToString() : null,
            ReceivedAtUtc = DateTime.UtcNow,
        };

        MessageStatusProcessingResult result = await _processor.ProcessAsync(update, cancellationToken);

        if (result.Kind == MessageStatusResultKind.Error)
        {
            _logger.LogError(
                "Status callback pipeline returned Error for sid={Sid}: {Reason}",
                update.MessageSid, result.Reason);
        }

        return Ok(new { status = "ok" });
    }
}
