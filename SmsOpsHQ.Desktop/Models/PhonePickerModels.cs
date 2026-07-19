using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Desktop.Models;

public enum PhonePickerAction
{
    SendSms,
    Call,
    OpenCustomer,
    SendDirections,
    RequestReview
}

public sealed record PhonePickerPresentation(
    string WindowTitle,
    string InstructionText,
    string ConfirmationText,
    string ConfirmationColor,
    string ConfirmationIcon);

public static class PhonePickerPresentations
{
    public static PhonePickerPresentation For(PhonePickerAction action) => action switch
    {
        PhonePickerAction.SendSms => new(
            "Send SMS — Choose Number",
            "Choose a number for SMS",
            "Send SMS",
            "#2563EB",
            "\uE8BD"),
        PhonePickerAction.Call => new(
            "Call — Choose Number",
            "Choose a number to call",
            "Call",
            "#059669",
            "\uE717"),
        PhonePickerAction.OpenCustomer => new(
            "Open Customer — Choose Number",
            "Choose a number to open",
            "Open",
            "#7C3AED",
            "\uE77B"),
        PhonePickerAction.SendDirections => new(
            "Send Directions — Choose Number",
            "Choose a number for directions",
            "Send Directions",
            "#EA580C",
            "\uE707"),
        PhonePickerAction.RequestReview => new(
            "Request Review — Choose Number",
            "Choose a number for the review request",
            "Request Review",
            "#D97706",
            "\uE734"),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}

public sealed record PhoneChoice(string PhoneE164, string DisplayPhone, string SourceLabel)
{
    public string DisplayText => string.IsNullOrWhiteSpace(SourceLabel)
        ? DisplayPhone
        : $"{SourceLabel}: {DisplayPhone}";
}

public static class PhoneChoiceBuilder
{
    public static IReadOnlyList<PhoneChoice> BuildCustomerChoices(
        string? homePhone,
        string? workPhone,
        string? notes,
        string? fallbackPhone = null)
    {
        List<PhoneChoice> choices = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        Add(choices, seen, homePhone, "Home");
        Add(choices, seen, workPhone, "Work");
        foreach (string phone in PhoneUtils.ExtractPhonesFromText(notes))
            Add(choices, seen, phone, "Notes");

        if (choices.Count == 0)
            Add(choices, seen, fallbackPhone, "Home");

        return choices;
    }

    public static IReadOnlyList<PhoneChoice> BuildUnlabeled(IEnumerable<string> phones)
    {
        List<PhoneChoice> choices = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        int labelIndex = 1;

        foreach (string phone in phones)
        {
            string label = $"Phone {labelIndex}";
            if (Add(choices, seen, phone, label))
                labelIndex++;
        }

        return choices;
    }

    public static string? SelectPhone(IReadOnlyList<PhoneChoice> choices, int selectedIndex)
    {
        return selectedIndex >= 0 && selectedIndex < choices.Count
            ? choices[selectedIndex].PhoneE164
            : null;
    }

    public static string FormatPhone(string phoneE164)
    {
        string digits = new(phoneE164.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1')
            digits = digits[1..];
        return digits.Length == 10
            ? $"({digits[..3]}) {digits[3..6]}-{digits[6..]}"
            : phoneE164;
    }

    private static bool Add(
        ICollection<PhoneChoice> choices,
        ISet<string> seen,
        string? phone,
        string sourceLabel)
    {
        string? normalized = PhoneUtils.NormalizeToE164(phone);
        if (normalized is null || !seen.Add(normalized))
            return false;

        choices.Add(new PhoneChoice(normalized, FormatPhone(normalized), sourceLabel));
        return true;
    }
}
