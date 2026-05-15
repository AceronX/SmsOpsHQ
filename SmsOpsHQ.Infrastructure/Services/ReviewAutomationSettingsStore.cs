using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Persists review automation options next to the SQLite database (same folder as smsops.db).
public sealed class ReviewAutomationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<ReviewAutomationSettingsStore> _logger;

    public ReviewAutomationSettingsStore(IConfiguration configuration, ILogger<ReviewAutomationSettingsStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string SettingsFilePath => Path.Combine(GetDatabaseDirectory(), "review_automation_settings.json");

    public ReviewAutomationSettings GetDefaults()
    {
        return new ReviewAutomationSettings
        {
            Enabled = _configuration.GetValue("ReviewAutomation:Enabled", false),
            IntervalMinutes = Math.Clamp(_configuration.GetValue("ReviewAutomation:IntervalMinutes", 30), 1, 24 * 60),
            RunOnStartup = _configuration.GetValue("ReviewAutomation:RunOnStartup", false)
        };
    }

    public ReviewAutomationSettings Load()
    {
        ReviewAutomationSettings defaults = GetDefaults();
        if (!File.Exists(SettingsFilePath))
            return defaults;

        try
        {
            string json = File.ReadAllText(SettingsFilePath);
            ReviewAutomationSettings? loaded = JsonSerializer.Deserialize<ReviewAutomationSettings>(json, JsonOptions);
            if (loaded is null)
                return defaults;

            loaded.IntervalMinutes = Math.Clamp(loaded.IntervalMinutes, 1, 24 * 60);
            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read review automation settings; using defaults.");
            return defaults;
        }
    }

    public void Save(ReviewAutomationSettings settings)
    {
        settings.IntervalMinutes = Math.Clamp(settings.IntervalMinutes, 1, 24 * 60);

        string dir = Path.GetDirectoryName(SettingsFilePath)!;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
        _logger.LogInformation("Saved review automation settings to {Path}", SettingsFilePath);
    }

    private string GetDatabaseDirectory()
    {
        string? sqlitePath = _configuration.GetSection("Database")["SqlitePath"];
        if (!string.IsNullOrWhiteSpace(sqlitePath))
        {
            string full = Path.GetFullPath(sqlitePath.Trim());
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                return dir;
        }

        string? conn = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            foreach (string part in conn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part[..eq].Trim();
                if (!key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                    continue;
                string path = part[(eq + 1)..].Trim();
                if (string.IsNullOrEmpty(path))
                    break;
                string full = Path.GetFullPath(path);
                string? dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
        }

        return AppContext.BaseDirectory;
    }
}
