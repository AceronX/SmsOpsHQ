using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists the configurable customer quality SQL query in AppData.
/// The query must use @customerKey as a parameter and return a single row
/// with named columns (each column becomes a quality metric display item).
/// </summary>
public sealed class CustomerQualityQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configPath;

    private const string DefaultQuery = @"
SELECT
    (SELECT COUNT(*) FROM Tickets
     WHERE CustomerKey = @customerKey AND HowClosed = 'CPU') AS cpu_count,
    (SELECT COUNT(*) FROM Tickets
     WHERE CustomerKey = @customerKey AND HowClosed = 'PFX-') AS pfx_count,
    (SELECT COUNT(*) FROM Tickets
     WHERE CustomerKey = @customerKey AND Active = 1 AND Type != 0
       AND DueDate IS NOT NULL AND DueDate != ''
       AND DATE(DueDate) < DATE('now')) AS late_tickets,
    COALESCE((SELECT AVG(JULIANDAY('now') - JULIANDAY(DueDate))
     FROM Tickets
     WHERE CustomerKey = @customerKey AND Active = 1 AND Type != 0
       AND DueDate IS NOT NULL AND DueDate != ''
       AND DATE(DueDate) < DATE('now')), 0) AS avg_days_late";

    public CustomerQualityQueryService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "customer_quality_query.json");
    }

    public string ConfigFilePath => _configPath;

    public string GetDefaultQuery() => DefaultQuery.Trim();

    public string LoadQuery()
    {
        if (!File.Exists(_configPath))
            return DefaultQuery.Trim();

        try
        {
            string json = File.ReadAllText(_configPath);
            var model = JsonSerializer.Deserialize<QueryConfigModel>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(model?.Query) ? DefaultQuery.Trim() : model.Query.Trim();
        }
        catch
        {
            return DefaultQuery.Trim();
        }
    }

    public void SaveQuery(string query)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var model = new QueryConfigModel
        {
            Query = query ?? DefaultQuery.Trim()
        };
        string json = JsonSerializer.Serialize(model, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private sealed class QueryConfigModel
    {
        public string Query { get; set; } = string.Empty;
    }
}
