namespace SmsOpsHQ.Core.DTOs;

// Result from IPhoneValidationService.ValidateMessage.
public sealed class PhoneValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public bool ShouldQuarantine { get; set; }
}
