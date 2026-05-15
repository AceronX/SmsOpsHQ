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
public sealed class TwilioService : ITwilioService
{
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioService> _logger;
    private readonly bool _mockMode;

    public TwilioService(IOptions<TwilioSettings> settings, ILogger<TwilioService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _mockMode = string.IsNullOrWhiteSpace(_settings.AccountSid)
                 || string.IsNullOrWhiteSpace(_settings.AuthToken);

        if (!_mockMode)
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
        // From must be a sender on that service when both are set.
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

        _logger.LogInformation(
            "Twilio MOCK send: SID={Sid} From={From} To={To} Body={Body}",
            mockSid, fromE164, toE164, body.Length > 50 ? body[..50] + "..." : body);

        return new TwilioSendResult
        {
            Success = true,
            TwilioSid = mockSid,
            Status = "Sent"
        };
    }
}
