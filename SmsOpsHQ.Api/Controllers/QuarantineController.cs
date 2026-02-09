using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

// HQ-only endpoints for reviewing and resolving quarantined messages.
// Ported from Python routes_quarantine.py.
[ApiController]
[Authorize]
[Route("api/quarantine")]
public sealed class QuarantineController : ControllerBase
{
    private readonly IQuarantineService _quarantineService;

    public QuarantineController(IQuarantineService quarantineService)
    {
        _quarantineService = quarantineService;
    }

    // GET /api/quarantine/list -- list quarantined messages for HQ review.
    [HttpGet("list")]
    public async Task<IActionResult> ListQuarantinedMessages(
        [FromQuery] int limit = 50,
        [FromQuery] string? resolution = null,
        CancellationToken cancellationToken = default)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Quarantine access requires HQ role");

        List<QuarantinedMessage> messages =
            await _quarantineService.GetMessagesAsync(limit, resolution, cancellationToken);

        return Ok(messages.Select(m => new
        {
            quarantine_id = m.QuarantineId,
            store_id = m.StoreId,
            from_phone = m.FromE164,
            to_phone = m.ToE164,
            body = m.Body,
            twilio_sid = m.TwilioSid,
            reason = m.QuarantineReason,
            quarantined_at = m.QuarantinedAt.ToString("o"),
            reviewed_at = m.ReviewedAt?.ToString("o"),
            reviewed_by_user_id = m.ReviewedByUserId,
            resolution = m.Resolution
        }));
    }

    // POST /api/quarantine/{quarantineId}/resolve -- resolve a quarantined message.
    [HttpPost("{quarantineId:int}/resolve")]
    public async Task<IActionResult> ResolveQuarantinedMessage(
        int quarantineId,
        [FromBody] ResolveQuarantineApiRequest body,
        CancellationToken cancellationToken)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Quarantine resolution requires HQ role");

        if (body.Action is not ("approve" or "reject"))
            return BadRequest(new { detail = "Action must be 'approve' or 'reject'" });

        bool resolved = await _quarantineService.ResolveAsync(
            quarantineId, body.Action, User.GetUserId(), cancellationToken);

        if (!resolved)
            return NotFound(new { detail = $"Quarantine ID {quarantineId} not found" });

        return Ok(new
        {
            success = true,
            message = $"Quarantine {quarantineId} resolved as '{body.Action}'",
            quarantine_id = quarantineId,
            action = body.Action
        });
    }

    // GET /api/quarantine/stats -- quarantine statistics by reason and resolution.
    [HttpGet("stats")]
    public async Task<IActionResult> GetQuarantineStats(CancellationToken cancellationToken)
    {
        if (!User.IsHqUser())
            return Problem(statusCode: 403, detail: "Quarantine stats require HQ role");

        // Get all messages for aggregation (pending + resolved).
        List<QuarantinedMessage> pending =
            await _quarantineService.GetMessagesAsync(limit: 10000, resolution: null, cancellationToken: cancellationToken);
        List<QuarantinedMessage> resolved =
            await _quarantineService.GetMessagesAsync(limit: 10000, resolution: "all", cancellationToken: cancellationToken);

        // Note: The current IQuarantineService filters by resolution. For full stats,
        // we return counts of the pending (unresolved) list since that's the default.
        return Ok(new
        {
            total = pending.Count + resolved.Count,
            pending = pending.Count,
            resolved = resolved.Count
        });
    }
}

public sealed class ResolveQuarantineApiRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
