namespace SmsOpsHQ.Core.DTOs;

public sealed class UpdateProfileRequest
{
    public string? Username { get; set; }
    public int? StoreId { get; set; }
    public int? TwilioNumberId { get; set; }
}
