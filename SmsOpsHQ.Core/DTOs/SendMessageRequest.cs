namespace SmsOpsHQ.Core.DTOs;

// Request body for POST /api/send.
public sealed class SendMessageRequest
{
    public int StoreId { get; set; }

    // Recipient phone (will be normalized to E.164)
    public string ToPhone { get; set; } = string.Empty;

    // Message text body
    public string Body { get; set; } = string.Empty;

    // Optional thread to append to; null creates a new thread
    public int? ThreadId { get; set; }

    // Optional specific Twilio number to send from; null uses store default
    public int? TwilioNumberId { get; set; }

    // Optional list of media URLs to attach
    public List<string>? MediaUrls { get; set; }
}
