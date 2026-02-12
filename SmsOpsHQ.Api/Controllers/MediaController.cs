using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Infrastructure.Services;

namespace SmsOpsHQ.Api.Controllers;

// Proxy, download, and upload media files (Twilio MMS attachments).
// Ported from Python routes_media.py.
[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController : ControllerBase
{
    private readonly MediaService _mediaService;
    private readonly ILogger<MediaController> _logger;

    public MediaController(MediaService mediaService, ILogger<MediaController> logger)
    {
        _mediaService = mediaService;
        _logger = logger;
    }

    // GET /api/media/proxy?url=... -- proxy Twilio media for frontend display.
    [HttpGet("proxy")]
    public async Task<IActionResult> ProxyMedia(
        [FromQuery] string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { detail = "URL is required" });

        if (!url.Contains("twilio.com") && !url.Contains("api.twilio.com"))
            return StatusCode(403, new { detail = "Invalid URL domain" });

        var result = await _mediaService.ProxyMediaAsync(url, cancellationToken);
        if (result is null)
            return StatusCode(502, new { detail = "Failed to fetch media" });

        return File(result.Value.Content, result.Value.ContentType);
    }

}
