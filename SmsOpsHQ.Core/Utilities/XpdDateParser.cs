using System.Globalization;

namespace SmsOpsHQ.Core.Utilities;

// Single parser for XPD / SQLite date strings used by API and PawnCalculator.
public static class XpdDateParser
{
    private static readonly string[] Formats =
    {
        "M/d/yyyy",
        "MM/dd/yyyy",
        "M/dd/yyyy",
        "MM/d/yyyy",
        "M/d/yy",
        "MM/dd/yy",
        "yyyy-MM-dd",
        "yyyy-M-d",
        "yyyy-MM-d",
        "yyyy-M-dd",
        "yyyy-MM-dd HH:mm:ss",
        "M/d/yyyy h:mm:ss tt",
        "MM/dd/yyyy h:mm:ss tt"
    };

    /// <summary>Parses date portion (before first space) or full string; returns date at midnight.</summary>
    public static bool TryParse(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        string datePart = trimmed.Contains(' ')
            ? trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0]
            : trimmed;

        if (DateTime.TryParseExact(
                datePart,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out date))
        {
            date = date.Date;
            return true;
        }

        if (datePart.Contains('/'))
        {
            string[] parts = datePart.Split('/');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int month) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
            {
                try
                {
                    date = new DateTime(year, month, day);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        if (DateTime.TryParse(datePart, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out date))
        {
            date = date.Date;
            return true;
        }

        return false;
    }
}
