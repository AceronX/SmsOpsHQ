using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Twilio;
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

            MessageResource message = await MessageResource.CreateAsync(
                to: new PhoneNumber(toE164),
                from: new PhoneNumber(fromE164),
                body: body,
                mediaUrl: mediaUris,
                statusCallback: callbackUri);

            _logger.LogInformation(
                "Twilio SMS sent: SID={Sid} From={From} To={To} Status={Status}",
                message.Sid, fromE164, toE164, message.Status);

            return new TwilioSendResult
            {
                Success = true,
                TwilioSid = message.Sid,
                Status = message.Status.ToString() ?? "Queued"
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
