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

    // Digits to send to a desk phone / PBX (Fanvil DIAL:…): 10-digit US, or short extension.
    // Strips a trailing "x123" / "ext 456" extension from the main number when present.
    public static string? GetDialString(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        string trimmed = phone.Trim();
        string mainPart = Regex.Replace(trimmed, @"(?i)\s*(?:x|ext\.?)\s*\d.*$", string.Empty).Trim();
        if (string.IsNullOrEmpty(mainPart))
            mainPart = trimmed;

        string digits = StripNonDigits(mainPart);
        if (digits.Length == 0)
            return null;

        if (digits.Length < 10)
            return digits;

        if (digits.Length == 11 && digits[0] == '1')
            return digits.Substring(1, 10);

        return digits.Substring(digits.Length - 10, 10);
    }

    private static readonly Regex PhoneInTextPattern = new Regex(
        @"\b1?[2-9]\d{2}[-.\s]?\d{3}[-.\s]?\d{4}\b|\b[2-9]\d{9}\b",
        RegexOptions.Compiled);

    public static List<string> ExtractPhonesFromText(string? text)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in PhoneInTextPattern.Matches(text))
        {
            string? normalized = ExtractLast10Digits(m.Value);
            if (normalized is not null && seen.Add(normalized))
                result.Add(normalized);
        }
        return result;
    }
}
