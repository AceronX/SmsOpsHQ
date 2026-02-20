using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists the SQL query configuration for late customers in a config file under AppData.
/// </summary>
public sealed class LateCustomersQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configPath;
    private const string DefaultQuery = @"
                SELECT
                    c.CustomerKey AS Key,
                    c.CustomerId,
                    c.FirstName,
                    c.LastName,
                    c.ResPhone,
                    c.BusPhone,
                    c.Notes AS CustomerNotes,
                    t.Key AS TicketKey,
                    t.TransNo,
                    t.DueDate,
                    t.CurrentBalance,
                    t.Amount,
                    t.Notes AS TicketNotes,
                    (SELECT COUNT(*) FROM Tickets t2 
                     WHERE t2.CustomerKey = c.CustomerKey 
                     AND t2.HowClosed LIKE 'PFX%') AS ForfeitCount,
                    GROUP_CONCAT(i.PrintedDetail, ' | ') AS Items,
                    GROUP_CONCAT(i.Notes, ' | ') AS ItemNotes,
                    GROUP_CONCAT(i.CategoryCode, ' | ') AS Category
                FROM Tickets t
                JOIN Customers c ON t.CustomerKey = c.CustomerKey
                LEFT JOIN Items i ON i.TicketKey = t.Key
                WHERE t.Type != 0
                  AND t.Active = 1
                  AND t.DueDate IS NOT NULL
                  AND t.DueDate != ''
                GROUP BY t.Key
                ORDER BY t.DueDate DESC
                LIMIT 5000";

    public LateCustomersQueryService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "late_customers_query.json");
        ConfigFolder = folder;
    }

    internal string ConfigFolder { get; }

    /// <summary>Path to the query config file (for diagnostics).</summary>
    public string ConfigFilePath => _configPath;

    /// <summary>Load SQL query from config file. Returns default query if file is missing or invalid.</summary>
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

    /// <summary>Save SQL query to config file. Creates folder if needed.</summary>
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
