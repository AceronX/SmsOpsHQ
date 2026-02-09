namespace SmsOpsHQ.Core.DTOs;

// Result from ITwilioService.SendSmsAsync.
public sealed class TwilioSendResult
{
    public bool Success { get; set; }
    public string? TwilioSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
