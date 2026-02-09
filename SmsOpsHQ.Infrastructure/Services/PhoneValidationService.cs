using System.Text.RegularExpressions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Infrastructure.Services;

// Validates inbound message metadata and body content.
// Dual-layer validation to prevent cross-store contamination:
//   1. Metadata validation: To or From must match the store phone.
//   2. Body validation: Detects store phone leakage in message body.
public sealed class PhoneValidationService : IPhoneValidationService
{
    // Regex to extract phone-like patterns from message text.
    private static readonly Regex PhonePattern = new Regex(
        @"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}",
        RegexOptions.Compiled);

    public PhoneValidationResult ValidateMessage(
        string toPhone, string fromPhone, string body,
        string storePhone, int storeId)
    {
        // Metadata validation: either To or From must be the store phone.
        string? storeLast10 = PhoneUtils.ExtractLast10Digits(storePhone);
        string? toLast10 = PhoneUtils.ExtractLast10Digits(toPhone);
        string? fromLast10 = PhoneUtils.ExtractLast10Digits(fromPhone);

        if (storeLast10 is null)
        {
            return new PhoneValidationResult
            {
                IsValid = false,
                FailureReason = "Store phone is invalid or not configured.",
                ShouldQuarantine = true
            };
        }

        bool toMatchesStore = toLast10 == storeLast10;
        bool fromMatchesStore = fromLast10 == storeLast10;

        if (!toMatchesStore && !fromMatchesStore)
        {
            return new PhoneValidationResult
            {
                IsValid = false,
                FailureReason = $"Neither To ({toPhone}) nor From ({fromPhone}) matches store phone ({storePhone}).",
                ShouldQuarantine = true
            };
        }

        // Body validation: check for store phone leakage in the message body.
        if (!string.IsNullOrWhiteSpace(body))
        {
            MatchCollection matches = PhonePattern.Matches(body);
            foreach (Match match in matches)
            {
                string? extractedLast10 = PhoneUtils.ExtractLast10Digits(match.Value);
                if (extractedLast10 is not null && extractedLast10 == storeLast10)
                {
                    return new PhoneValidationResult
                    {
                        IsValid = false,
                        FailureReason = $"Message body contains the store phone number ({storePhone}). Possible cross-store contamination.",
                        ShouldQuarantine = true
                    };
                }
            }
        }

        return new PhoneValidationResult { IsValid = true };
    }
}
