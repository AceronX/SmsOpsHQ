using System.Text.RegularExpressions;

namespace SmsOpsHQ.Core.Utilities;

// US phone number utilities. No external dependencies.
// Storage format: E.164 (+1XXXXXXXXXX). Matching format: last 10 digits.
public static class PhoneUtils
{
    // Strips everything except digits from the input.
    private static string StripNonDigits(string input)
    {
        return Regex.Replace(input, @"[^\d]", "");
    }

    // Extracts the last 10 digits from a phone number string.
    // Used for matching (e.g. CustomerPhones.PhoneNormalized).
    // Returns null if fewer than 10 digits are present.
    public static string? ExtractLast10Digits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        string digits = StripNonDigits(phone);

        if (digits.Length < 10)
            return null;

        return digits.Substring(digits.Length - 10);
    }

    // Normalizes a US phone number to E.164 format (+1XXXXXXXXXX).
    // Accepts formats like: 7185551234, 17185551234, +17185551234,
    // (718) 555-1234, 718-555-1234, 1-718-555-1234, etc.
    // Returns null if the input cannot be normalized to a valid 10-digit US number.
    public static string? NormalizeToE164(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        string digits = StripNonDigits(phone);

        // 10 digits: assume US number without country code.
        if (digits.Length == 10)
            return "+1" + digits;

        // 11 digits starting with 1: US number with country code.
        if (digits.Length == 11 && digits[0] == '1')
            return "+" + digits;

        return null;
    }

    // Returns true if the input can be normalized to a valid US E.164 number.
    public static bool IsValidUsPhone(string? phone)
    {
        return NormalizeToE164(phone) is not null;
    }
}
