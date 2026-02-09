using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Downloads Twilio MMS media and stores files locally.
// Ported from Python media_service.py.
public sealed class MediaService : IMediaService
{
    private readonly TwilioSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaService> _logger;

    public MediaService(
        IOptions<TwilioSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<MediaService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> DownloadMediaAsync(string mediaUrl, string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safePhone = phoneNumber.Replace("+", "").Trim();
        if (string.IsNullOrEmpty(safePhone))
            safePhone = "unknown";

        HttpClient client = _httpClientFactory.CreateClient();

        // Authenticate with Twilio credentials when available.
        if (!string.IsNullOrWhiteSpace(_settings.AccountSid) &&
            !string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            byte[] credBytes = System.Text.Encoding.ASCII.GetBytes(
                $"{_settings.AccountSid}:{_settings.AuthToken}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credBytes));
        }

        HttpResponseMessage response = await client.GetAsync(mediaUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        string extension = GuessExtension(contentType);

        string filename = $"{safePhone}_{timestamp}{extension}";
        string downloadsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloadsDir);

        string filePath = Path.Combine(downloadsDir, filename);

        await using FileStream fileStream = File.Create(filePath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Downloaded media to {Path} ({ContentType})", filePath, contentType);
        return filePath;
    }

    // Proxy media content for browser display without exposing Twilio credentials.
    public async Task<(byte[] Content, string ContentType)?> ProxyMediaAsync(
        string mediaUrl, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient();

        if (!string.IsNullOrWhiteSpace(_settings.AccountSid) &&
            !string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            byte[] credBytes = System.Text.Encoding.ASCII.GetBytes(
                $"{_settings.AccountSid}:{_settings.AuthToken}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credBytes));
        }

        HttpResponseMessage response = await client.GetAsync(mediaUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

        return (content, contentType);
    }

    private static string GuessExtension(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }
}
