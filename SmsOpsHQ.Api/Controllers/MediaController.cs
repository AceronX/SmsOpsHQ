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

    // POST /api/media/download?url=...&phone=... -- download media to local storage.
    [HttpPost("download")]
    public async Task<IActionResult> DownloadMedia(
        [FromQuery] string url,
        [FromQuery] string? phone = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { detail = "URL is required" });

        try
        {
            string localPath = await _mediaService.DownloadMediaAsync(url, phone ?? "unknown", cancellationToken);
            return Ok(new { path = localPath });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media download failed for {Url}", url);
            return StatusCode(500, new { detail = "Failed to download media" });
        }
    }

    // POST /api/media/upload -- upload a file to static storage.
    [HttpPost("upload")]
    public async Task<IActionResult> UploadMedia(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { detail = "No file provided" });

        string extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension))
            extension = ".png";

        string filename = $"upload_{Guid.NewGuid():N}{extension}";

        string staticDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "media");
        Directory.CreateDirectory(staticDir);

        string filePath = Path.Combine(staticDir, filename);

        await using FileStream stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream, cancellationToken);

        return Ok(new { url = $"/media/{filename}" });
    }
}
