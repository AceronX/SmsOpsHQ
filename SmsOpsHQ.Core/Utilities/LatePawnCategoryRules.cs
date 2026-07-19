namespace SmsOpsHQ.Core.Utilities;

/// <summary>
/// Shared parsing and classification rules for categories returned by the late-pawn query.
/// </summary>
public static class LatePawnCategoryRules
{
    public const string Jewelry = "JEWELRY";
    public const string Electronics = "ELECTRONICS";
    public const string General = "GENERAL";

    /// <summary>
    /// Splits the query's pipe-delimited category value, trims it, removes blanks, and
    /// deduplicates values case-insensitively while preserving the first display value.
    /// </summary>
    public static IReadOnlyList<string> ParseAggregated(string? aggregatedCategories)
    {
        if (string.IsNullOrWhiteSpace(aggregatedCategories))
            return Array.Empty<string>();

        return Normalize(aggregatedCategories.Split('|'));
    }

    /// <summary>
    /// Normalizes category values from an API array. Pipe-delimited values are also
    /// accepted so older/custom query results remain compatible.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? categories)
    {
        if (categories is null)
            return Array.Empty<string>();

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = new();

        foreach (string? value in categories)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (string part in value.Split('|'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed))
                    normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    public static bool Contains(IEnumerable<string>? categories, string category) =>
        categories?.Any(value => string.Equals(value, category, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Returns the current category portion of the late-pawn risk score.
    /// Electronics/General takes precedence over Jewelry for mixed-category tickets.
    /// </summary>
    public static int GetRiskPoints(IEnumerable<string>? categories)
    {
        if (Contains(categories, Electronics) || Contains(categories, General))
            return 10;

        return Contains(categories, Jewelry) ? 5 : 0;
    }
}
