using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Contract for validating inbound message metadata and content.
public interface IPhoneValidationService
{
    // Validate message phones and body content.
    // Checks for store phone leakage in body and metadata consistency.
    PhoneValidationResult ValidateMessage(
        string toPhone, string fromPhone, string body,
        string storePhone, int storeId);
}
