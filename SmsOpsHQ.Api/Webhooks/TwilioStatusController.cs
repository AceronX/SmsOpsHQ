using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Webhooks;

// Handles Twilio delivery status callbacks. No [Authorize] -- Twilio sends these.
// Route: POST /twilio-sms/status
//
// Updates message delivery status (delivered, failed, undelivered, etc.)
// and pushes a realtime notification so the desktop client updates the UI.
[ApiController]
[Route("twilio-sms")]
public sealed class TwilioStatusController : ControllerBase
{
    private readonly IMessageRepository _messageRepo;
    private readonly IRealtimeService _realtimeService;
    private readonly ILogger<TwilioStatusController> _logger;

    public TwilioStatusController(
        IMessageRepository messageRepo,
        IRealtimeService realtimeService,
        ILogger<TwilioStatusController> logger)
    {
        _messageRepo = messageRepo;
        _realtimeService = realtimeService;
        _logger = logger;
    }

    [HttpPost("status")]
    public async Task<IActionResult> HandleStatus(CancellationToken cancellationToken)
    {
        IFormCollection form = await Request.ReadFormAsync(cancellationToken);

        string messageSid = form["MessageSid"].ToString();
        string messageStatus = form["MessageStatus"].ToString();
        string? errorCode = form.ContainsKey("ErrorCode") ? form["ErrorCode"].ToString() : null;

        if (string.IsNullOrEmpty(messageSid))
            return Ok(new { status = "ok" });

        // Capitalize status to match convention (Twilio sends lowercase: "delivered", "failed")
        string normalizedStatus = char.ToUpper(messageStatus[0]) + messageStatus[1..];

        // Update message in DB
        await _messageRepo.UpdateStatusBySidAsync(
            messageSid, normalizedStatus, errorCode, null, cancellationToken);

        // Look up the message to get StoreId/ThreadId for realtime push
        Message? message = await _messageRepo.FindBySidAsync(messageSid, cancellationToken);
        if (message is not null)
        {
            await _realtimeService.PushMessageStatusAsync(
                message.StoreId,
                message.ThreadId,
                message.MessageId,
                messageSid,
                normalizedStatus,
                errorCode,
                cancellationToken);
        }

        return Ok(new { status = "ok" });
    }
}
