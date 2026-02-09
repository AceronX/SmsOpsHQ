using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for sending SMS via the Twilio API.
public interface ITwilioService
{
    // Send an SMS message. Returns the Twilio SID and delivery status.
    // Supports mock mode when credentials are not configured.
    Task<TwilioSendResult> SendSmsAsync(
        string fromE164, string toE164, string body,
        List<string>? mediaUrls = null, string? statusCallbackUrl = null,
        CancellationToken cancellationToken = default);
}
