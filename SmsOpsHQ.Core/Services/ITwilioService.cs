using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for sending SMS via the Twilio API.
public interface ITwilioService
{
    /// <summary>
    /// True when AccountSid or AuthToken are not configured. In mock mode no
    /// HTTP request is made to Twilio — outbound messages are NOT delivered.
    /// </summary>
    bool IsMockMode { get; }

    /// <summary>The first 6 chars of the configured AccountSid (for diagnostics), or empty.</summary>
    string AccountSidPrefix { get; }

    /// <summary>True when a Messaging Service SID is configured (US A2P / 10DLC).</summary>
    bool HasMessagingService { get; }

    // Send an SMS message. Returns the Twilio SID and delivery status.
    // Supports mock mode when credentials are not configured.
    Task<TwilioSendResult> SendSmsAsync(
        string fromE164, string toE164, string body,
        List<string>? mediaUrls = null, string? statusCallbackUrl = null,
        CancellationToken cancellationToken = default);
}
