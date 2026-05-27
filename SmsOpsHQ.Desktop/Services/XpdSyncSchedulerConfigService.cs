using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists the hourly XPD auto-sync scheduler settings (Enabled, IntervalMinutes,
/// RunOnStartup) to <c>%AppData%\SmsOpsHQ\xpd_sync_config.json</c>.
///
/// The shape mirrors the <c>XpdSync</c> section of <c>appsettings.json</c> so the
/// bundled API can pick this file up via <c>AddJsonFile</c> and have its values
/// overlay whatever was shipped in appsettings -- exactly like
/// <see cref="HubConfigService"/> does for the Hub settings.
///
/// Result: an installer can ship <c>"XpdSync": { "Enabled": false }</c> and the
/// operator turns hourly sync on once from Settings -> XPD without ever editing
/// JSON files by hand. Atomic write so a crash mid-save never produces a
/// half-rewritten file.
/// </summary>
public sealed class XpdSyncSchedulerConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null // Keep PascalCase to match appsettings.json
    };

    private readonly string _configPath;

    public XpdSyncSchedulerConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "xpd_sync_config.json");
        ConfigFolder = folder;
    }

    internal string ConfigFolder { get; }

    public string ConfigFilePath => _configPath;

    /// <summary>True if the file currently exists. UI shows "(not configured)" when false.</summary>
    public bool Exists => File.Exists(_configPath);

    /// <summary>
    /// Load scheduler settings. Returns a model with safe defaults
    /// (Enabled=false, Interval=60, RunOnStartup=false) when the file is
    /// missing or invalid -- callers do NOT need to null-check.
    /// </summary>
    public XpdSyncSchedulerConfigModel Load()
    {
        if (!File.Exists(_configPath))
            return new XpdSyncSchedulerConfigModel();

        try
        {
            string json = File.ReadAllText(_configPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("XpdSync", out JsonElement xs))
                return new XpdSyncSchedulerConfigModel();

            XpdSyncSchedulerConfigModel model = new();
            if (xs.TryGetProperty("Enabled", out JsonElement enabled)
                && enabled.ValueKind == JsonValueKind.True)
                model.Enabled = true;
            if (xs.TryGetProperty("IntervalMinutes", out JsonElement im)
                && im.ValueKind == JsonValueKind.Number
                && im.TryGetInt32(out int iv) && iv >= 1)
                model.IntervalMinutes = iv;
            if (xs.TryGetProperty("RunOnStartup", out JsonElement ros)
                && ros.ValueKind == JsonValueKind.True)
                model.RunOnStartup = true;

            return model;
        }
        catch
        {
            // Corrupt file shouldn't break the Settings UI -- fall back to defaults
            // and let the user re-save (which writes a clean file atomically).
            return new XpdSyncSchedulerConfigModel();
        }
    }

    /// <summary>Save scheduler settings atomically.</summary>
    public void Save(XpdSyncSchedulerConfigModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Wrap in {"XpdSync": {...}} so the API can layer this directly on top
        // of appsettings.json via AddJsonFile (no custom merge logic on the
        // server). Clamp interval here so a typo'd Save can't write garbage.
        int interval = model.IntervalMinutes < 1 ? 60 : model.IntervalMinutes;
        var doc = new
        {
            XpdSync = new
            {
                model.Enabled,
                IntervalMinutes = interval,
                model.RunOnStartup
            }
        };

        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(doc, JsonOptions);
        string tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_configPath))
            File.Replace(tempPath, _configPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, _configPath);
    }

    public sealed class XpdSyncSchedulerConfigModel
    {
        public bool Enabled { get; set; }
        public int IntervalMinutes { get; set; } = 60;
        public bool RunOnStartup { get; set; }
    }
}
