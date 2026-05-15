using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace SmsOpsHQ.Infrastructure.Services;

// Sends SMS via the Twilio REST API. Supports mock mode when credentials
// are not configured (AccountSid or AuthToken empty).
//
// IMPORTANT: This service is registered scoped and resolves IOptionsSnapshot
// so credential changes (e.g. saved by the desktop UI to the AppData JSON
// file) take effect on the next request without an API restart.
public sealed class TwilioService : ITwilioService
{
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioService> _logger;
    private readonly bool _mockMode;

    // We log the "MOCK mode" warning at most once every 30s per process so
    // that callers never miss it but logs aren't flooded under load.
    private static DateTime _lastMockBannerLoggedUtc = DateTime.MinValue;
    private static readonly object _bannerLock = new();

    // Production code constructs this service through the factory registered in
    // DependencyInjection.AddInfrastructure, which resolves IOptionsSnapshot so that
    // credential changes flow through per-request without an API restart.
    // Unit tests construct it directly via Options.Create(...).
    public TwilioService(IOptions<TwilioSettings> settings, ILogger<TwilioService> logger)
        : this(settings.Value, logger)
    {
    }

    /// <summary>
    /// Factory used by DI: forwards the per-scope IOptionsSnapshot value into the
    /// service. Kept here so both DependencyInjection and any other host (Desktop's
    /// LocalApiHost, integration tests) build the service the same way.
    /// </summary>
    public static TwilioService Create(IServiceProvider sp)
    {
        IOptionsSnapshot<TwilioSettings> snapshot =
            sp.GetRequiredService<IOptionsSnapshot<TwilioSettings>>();
        ILogger<TwilioService> logger =
            sp.GetRequiredService<ILogger<TwilioService>>();
        return new TwilioService(Options.Create(snapshot.Value), logger);
    }

    private TwilioService(TwilioSettings settings, ILogger<TwilioService> logger)
    {
        _settings = settings;
        _logger = logger;
        _mockMode = string.IsNullOrWhiteSpace(_settings.AccountSid)
                 || string.IsNullOrWhiteSpace(_settings.AuthToken);

        if (_mockMode)
        {
            LogMockBannerOnce();
        }
        else
        {
            TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
            if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                _logger.LogInformation(
                    "Twilio Messaging Service enabled for outbound SMS: {MessagingServiceSid}",
                    _settings.MessagingServiceSid);
            }
        }
    }

    public bool IsMockMode => _mockMode;

    public string AccountSidPrefix =>
        string.IsNullOrEmpty(_settings.AccountSid)
            ? string.Empty
            : _settings.AccountSid.Length <= 6
                ? _settings.AccountSid
                : _settings.AccountSid.Substring(0, 6);

    public bool HasMessagingService => !string.IsNullOrWhiteSpace(_settings.MessagingServiceSid);

    public async Task<TwilioSendResult> SendSmsAsync(
        string fromE164, string toE164, string body,
        List<string>? mediaUrls = null, string? statusCallbackUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (_mockMode)
        {
            return MockSend(fromE164, toE164, body);
        }

        try
        {
            List<Uri>? mediaUris = mediaUrls?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => new Uri(u))
                .ToList();

            Uri? callbackUri = string.IsNullOrWhiteSpace(statusCallbackUrl)
                ? null
                : new Uri(statusCallbackUrl);

            MessageResource message = await CreateMessageAsync(
                fromE164, toE164, body, mediaUris, callbackUri).ConfigureAwait(false);

            _logger.LogInformation(
                "Twilio SMS sent: SID={Sid} From={From} To={To} Status={Status} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage}",
                message.Sid, fromE164, toE164, message.Status, message.ErrorCode, message.ErrorMessage);

            // Twilio can return 2xx with a non-deliverable terminal status on the resource (e.g. carrier block).
            string status = message.Status.ToString() ?? "Unknown";
            bool terminalFailure = IsTerminalUndeliverable(status, message.ErrorCode);
            if (terminalFailure)
            {
                _logger.LogWarning(
                    "Twilio message created but not deliverable: Sid={Sid} Status={Status} ErrorCode={ErrorCode} Message={Message}",
                    message.Sid, status, message.ErrorCode, message.ErrorMessage);
            }

            return new TwilioSendResult
            {
                Success = !terminalFailure,
                TwilioSid = message.Sid,
                Status = status,
                ErrorCode = terminalFailure
                    ? (message.ErrorCode != 0 ? message.ErrorCode.ToString() : "UNDELIVERABLE")
                    : null,
                ErrorMessage = terminalFailure ? (message.ErrorMessage ?? status) : null
            };
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Twilio API error: Code={Code} Status={Status} From={From} To={To}",
                ex.Code, ex.Status, fromE164, toE164);

            return new TwilioSendResult
            {
                Success = false,
                Status = "Failed",
                ErrorCode = ex.Code.ToString(),
                ErrorMessage = string.IsNullOrWhiteSpace(ex.Message) ? ex.MoreInfo : ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio SMS failed: From={From} To={To}", fromE164, toE164);

            return new TwilioSendResult
            {
                Success = false,
                Status = "Failed",
                ErrorCode = "TWILIO_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<MessageResource> CreateMessageAsync(
        string fromE164,
        string toE164,
        string body,
        List<Uri>? mediaUris,
        Uri? callbackUri)
    {
        // US A2P / 10DLC: sending through a Messaging Service (MG…) is the supported path when configured.
        // Per Twilio docs, when both `From` and `MessagingServiceSid` are set, `From` takes precedence
        // as long as it belongs to the messaging service's sender pool.
        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            return await MessageResource.CreateAsync(
                to: new PhoneNumber(toE164),
                from: new PhoneNumber(fromE164),
                body: body,
                messagingServiceSid: _settings.MessagingServiceSid,
                mediaUrl: mediaUris,
                statusCallback: callbackUri).ConfigureAwait(false);
        }

        return await MessageResource.CreateAsync(
            to: new PhoneNumber(toE164),
            from: new PhoneNumber(fromE164),
            body: body,
            mediaUrl: mediaUris,
            statusCallback: callbackUri).ConfigureAwait(false);
    }

    private static bool IsTerminalUndeliverable(string status, int? errorCode)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase))
            return true;

        // Twilio sometimes returns "canceled" for blocked traffic.
        if (string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
            return true;

        // High-signal carrier / compliance failures on create (when populated).
        if (errorCode is int code && code is >= 30000 and <= 32000)
            return true;

        return false;
    }

    private TwilioSendResult MockSend(string fromE164, string toE164, string body)
    {
        string mockSid = $"SM_MOCK_{Guid.NewGuid():N}".Substring(0, 34);

        // WARNING-level: the operator MUST notice that real delivery did not happen.
        _logger.LogWarning(
            "Twilio MOCK send (NOT delivered to carrier — credentials missing): SID={Sid} From={From} To={To} Body={Body}",
            mockSid, fromE164, toE164, body.Length > 50 ? body[..50] + "..." : body);

        LogMockBannerOnce();

        return new TwilioSendResult
        {
            Success = true,
            TwilioSid = mockSid,
            // Status reflects reality: this message was NOT sent.
            Status = "Mock",
            IsMock = true,
            ErrorCode = "MOCK_MODE",
            ErrorMessage = "Twilio is in MOCK mode — message was not delivered. Configure AccountSid and AuthToken in Settings → Twilio."
        };
    }

    private void LogMockBannerOnce()
    {
        // Throttle to at most once every 30 seconds across the process so that
        // the warning is hard to miss but doesn't drown out other logs.
        DateTime now = DateTime.UtcNow;
        lock (_bannerLock)
        {
            if ((now - _lastMockBannerLoggedUtc).TotalSeconds < 30) return;
            _lastMockBannerLoggedUtc = now;
        }

        _logger.LogWarning(
            "============================================================");
        _logger.LogWarning(
            " TWILIO MOCK MODE ACTIVE — outbound SMS will NOT be delivered.");
        _logger.LogWarning(
            " Configure AccountSid + AuthToken in appsettings.json (Twilio");
        _logger.LogWarning(
            " section) or via the desktop app: Settings → Twilio → Save.");
        _logger.LogWarning(
            "============================================================");
    }
}
