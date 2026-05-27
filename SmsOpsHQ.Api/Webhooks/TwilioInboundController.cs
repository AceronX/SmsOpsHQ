using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Webhooks;

// Handles inbound SMS sent directly to this store by Twilio (legacy /per-store
// webhook path or local dev). No [Authorize] -- Twilio sends unauthenticated webhooks.
// Route: POST /twilio-sms
//
// The full inbound pipeline lives in IInboundSmsProcessor so the central Hub
// SignalR receiver can run the exact same logic (see Phase 5).
[ApiController]
[Route("twilio-sms")]
public sealed class TwilioInboundController : ControllerBase
{
    private readonly IInboundSmsProcessor _processor;
    private readonly ILogger<TwilioInboundController> _logger;

    public TwilioInboundController(
        IInboundSmsProcessor processor,
        ILogger<TwilioInboundController> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleInbound(CancellationToken cancellationToken)
    {
        IFormCollection form = await Request.ReadFormAsync(cancellationToken);

        InboundSmsRequest request = new()
        {
            MessageSid = form["MessageSid"].ToString(),
            From = form["From"].ToString(),
            To = form["To"].ToString(),
            Body = form["Body"].ToString(),
            NumMedia = int.TryParse(form["NumMedia"], out int nm) ? nm : 0,
            ReceivedAtUtc = DateTime.UtcNow,
        };

        for (int i = 0; i < request.NumMedia; i++)
        {
            string url = form[$"MediaUrl{i}"].ToString();
            if (string.IsNullOrEmpty(url)) continue;
            string? contentType = form.ContainsKey($"MediaContentType{i}")
                ? form[$"MediaContentType{i}"].ToString()
                : null;
            request.Media.Add(new InboundMediaItem
            {
                Index = i,
                Url = url,
                ContentType = contentType,
            });
        }

        InboundSmsProcessingResult result = await _processor.ProcessAsync(request, cancellationToken);

        // Always return empty TwiML 200. Failures (NoStoreMatch, Rejected, etc.)
        // are visible via Serilog; returning non-200 would only cause Twilio retries.
        if (result.Kind == InboundSmsResultKind.Error)
        {
            _logger.LogError(
                "Inbound SMS pipeline returned Error for sid={Sid}: {Reason}",
                request.MessageSid, result.Reason);
        }

        return TwimlOk();
    }

    private ContentResult TwimlOk() => Content("<Response></Response>", "application/xml");
}
