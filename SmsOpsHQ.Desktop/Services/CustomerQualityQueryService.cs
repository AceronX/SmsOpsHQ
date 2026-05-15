namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Legacy hook for customer quality; the API uses server-side named metrics (<c>qualityMetric: default</c>).
/// </summary>
public sealed class CustomerQualityQueryService
{
    public string ConfigFilePath => string.Empty;

    public string GetDefaultQuery() => "default";

    public string LoadQuery() => "default";

    public void SaveQuery(string query) => _ = query;
}
