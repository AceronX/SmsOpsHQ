using System.Text.RegularExpressions;

namespace SmsOpsHQ.Core.Utilities;

// Auto-classifies SMS message bodies into categories.
// Priority order: directions > promotions > reminder > general.
// Ported from Python message_classifier.py.
public static class MessageClassifier
{
    // Substring patterns checked against lowercased body (case-insensitive).
    // Any match returns "directions" immediately.
    private static readonly string[] DirectionPatterns =
    {
        "maps.google.com",
        "goo.gl/maps",
        "google.com/maps",
        "apple.com/maps",
        "maps.apple.com",
        "directions",
        "click here for directions",
        "get directions",
        "find us at",
        "located at"
    };

    // Substring patterns for promotional messages.
    // Any match returns "promotions" (if no direction match first).
    private static readonly string[] PromotionPatterns =
    {
        "\u2B50",          // star emoji
        "rated!!",
        "do you have pawns at another pawnshop",
        "visit king gold and pawn",
        "top prices",
        "trusted for",
        "best prices",
        "highest prices",
        "we pay more",
        "bring your items",
        "special offer",
        "limited time",
        "sale",
        "% off"
    };

    // Regex patterns for ticket number references.
    // Any match returns "reminder" (if no direction or promotion match first).
    private static readonly Regex[] ReminderPatterns =
    {
        new Regex(@"ticket\s*#:?\s*\d+", RegexOptions.Compiled),
        new Regex(@"#\d{5,6}", RegexOptions.Compiled),
        new Regex(@"ticket\s+\d{5,6}", RegexOptions.Compiled),
        new Regex(@"pawn\s+#?\d{5,6}", RegexOptions.Compiled)
    };

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        { "reminder", "Reminders" },
        { "directions", "Directions" },
        { "promotions", "Promotions" },
        { "general", "General" }
    };

    // Classify a message body into a category.
    // Returns "directions", "promotions", "reminder", or "general".
    public static string Classify(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "general";

        string bodyLower = body.ToLowerInvariant();

        // Check directions first (highest priority)
        foreach (string pattern in DirectionPatterns)
        {
            if (bodyLower.Contains(pattern, StringComparison.Ordinal))
                return "directions";
        }

        // Check promotions second
        foreach (string pattern in PromotionPatterns)
        {
            if (bodyLower.Contains(pattern, StringComparison.Ordinal))
                return "promotions";
        }

        // Check reminders third (regex patterns)
        foreach (Regex pattern in ReminderPatterns)
        {
            if (pattern.IsMatch(bodyLower))
                return "reminder";
        }

        return "general";
    }

    // Get human-readable display name for a category code.
    public static string GetDisplayName(string category)
    {
        return DisplayNames.GetValueOrDefault(category, "General");
    }

    // Get all valid category codes.
    public static List<string> GetValidCategories()
    {
        return new List<string> { "reminder", "directions", "promotions", "general" };
    }
}
