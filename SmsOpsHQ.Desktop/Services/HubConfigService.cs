using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists the HQ Hub connection settings (URL, store API key, deployment id,
/// heartbeat cadence) in <c>%AppData%\SmsOpsHQ\hub_config.json</c>.
///
/// The shape mirrors the appsettings.json "Hub" section so the bundled API
/// can pick this file up via <c>AddJsonFile</c> and have its values overlay
/// whatever was shipped in appsettings. Result: an installer can ship empty
/// Hub fields and the operator configures them once via the WPF dialog.
///
/// Mirrors the <see cref="TwilioConfigService"/> pattern -- atomic write,
/// best-effort parsing, never throws on a missing/corrupt file (defaults are
/// safe).
/// </summary>
public sealed class HubConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null // Keep PascalCase to match appsettings.json
    };

    private readonly string _configPath;

    public HubConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "hub_config.json");
        ConfigFolder = folder;
    }

    internal string ConfigFolder { get; }

    public string ConfigFilePath => _configPath;

    /// <summary>True if the file currently exists. UI shows "(not configured)" when false.</summary>
    public bool Exists => File.Exists(_configPath);

    /// <summary>
    /// Load Hub settings. Returns a model with safe defaults when the file is
    /// missing or invalid.
    /// </summary>
    public HubConfigModel Load()
    {
        if (!File.Exists(_configPath))
            return new HubConfigModel();

        try
        {
            string json = File.ReadAllText(_configPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Hub", out JsonElement hub))
                return new HubConfigModel();

            HubConfigModel model = new();
            if (hub.TryGetProperty("Enabled", out JsonElement enabled)
                && enabled.ValueKind == JsonValueKind.True)
                model.Enabled = true;
            if (hub.TryGetProperty("Url", out JsonElement url) && url.ValueKind == JsonValueKind.String)
                model.Url = url.GetString() ?? string.Empty;
            if (hub.TryGetProperty("StoreKey", out JsonElement key) && key.ValueKind == JsonValueKind.String)
                model.StoreKey = key.GetString() ?? string.Empty;
            if (hub.TryGetProperty("DeploymentId", out JsonElement dep) && dep.ValueKind == JsonValueKind.String)
                model.DeploymentId = dep.GetString() ?? string.Empty;
            if (hub.TryGetProperty("IntervalSeconds", out JsonElement secs)
                && secs.ValueKind == JsonValueKind.Number
                && secs.TryGetInt32(out int s))
                model.IntervalSeconds = s;

            return model;
        }
        catch
        {
            return new HubConfigModel();
        }
    }

    /// <summary>Save all Hub settings atomically.</summary>
    public void Save(HubConfigModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Wrap in {"Hub": {...}} so appsettings-style readers (AddJsonFile)
        // can layer this directly on top of appsettings.json without any
        // custom merging logic on the API side.
        var doc = new
        {
            Hub = new
            {
                model.Enabled,
                model.Url,
                model.StoreKey,
                model.DeploymentId,
                model.IntervalSeconds
            }
        };

        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(doc, JsonOptions);
        // Atomic replace: write a temp file then move it over so a crash mid-write
        // never leaves a half-rewritten file (the API reads this on startup).
        string tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_configPath))
            File.Replace(tempPath, _configPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, _configPath);
    }

    public sealed class HubConfigModel
    {
        public bool Enabled { get; set; }
        public string Url { get; set; } = string.Empty;
        public string StoreKey { get; set; } = string.Empty;
        public string DeploymentId { get; set; } = string.Empty;
        public int IntervalSeconds { get; set; } = 60;
    }
}
