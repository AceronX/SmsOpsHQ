using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists Twilio Account SID, Auth Token, and Messaging Service SID in a config file
/// under AppData. Load on app start / when opening Settings; save when user saves Twilio
/// settings. The API project reads this same file at startup (and per-request via
/// IOptionsSnapshot), so credentials saved here flow through to outbound SMS without
/// editing appsettings.json.
/// </summary>
public sealed class TwilioConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configPath;

    public TwilioConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "twilio_config.json");
        ConfigFolder = folder;
    }

    internal string ConfigFolder { get; }

    /// <summary>Path to the Twilio config file (for diagnostics).</summary>
    public string ConfigFilePath => _configPath;

    /// <summary>
    /// Load all Twilio settings from the config file. Returns a model with empty strings
    /// when the file is missing or invalid (never null) so callers can bind directly.
    /// </summary>
    public TwilioConfigModel Load()
    {
        if (!File.Exists(_configPath))
            return new TwilioConfigModel();

        try
        {
            string json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<TwilioConfigModel>(json, JsonOptions)
                ?? new TwilioConfigModel();
        }
        catch
        {
            return new TwilioConfigModel();
        }
    }

    /// <summary>Backwards-compatible Load returning just the credentials tuple.</summary>
    public (string? AccountSid, string? AuthToken) LoadCredentials()
    {
        TwilioConfigModel m = Load();
        return (m.AccountSid, m.AuthToken);
    }

    /// <summary>
    /// Save Account SID and Auth Token. Preserves any other fields already in the file
    /// (notably MessagingServiceSid) so saving from the credentials section doesn't wipe
    /// the messaging service configured elsewhere.
    /// </summary>
    public void Save(string accountSid, string authToken)
    {
        TwilioConfigModel existing = Load();
        existing.AccountSid = accountSid ?? string.Empty;
        existing.AuthToken = authToken ?? string.Empty;
        WriteAtomic(existing);
    }

    /// <summary>
    /// Save the full Twilio configuration including the optional Messaging Service SID.
    /// </summary>
    public void Save(string accountSid, string authToken, string messagingServiceSid)
    {
        TwilioConfigModel existing = Load();
        existing.AccountSid = accountSid ?? string.Empty;
        existing.AuthToken = authToken ?? string.Empty;
        existing.MessagingServiceSid = messagingServiceSid ?? string.Empty;
        WriteAtomic(existing);
    }

    private void WriteAtomic(TwilioConfigModel model)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(model, JsonOptions);
        // Write to a temp file then move to avoid leaving a half-written file
        // if the process crashes mid-write (the API reads this file at runtime).
        string tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_configPath))
            File.Replace(tempPath, _configPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, _configPath);
    }

    public sealed class TwilioConfigModel
    {
        public string AccountSid { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public string MessagingServiceSid { get; set; } = string.Empty;
    }
}
