using System.IO;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Persists Twilio Account SID and Auth Token in a config file under AppData.
/// Load on app start / when opening Settings; save when user saves Twilio settings.
/// </summary>
public sealed class TwilioConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
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

    /// <summary>Load Account SID and Auth Token from config file. Returns (null, null) if file is missing or invalid.</summary>
    public (string? AccountSid, string? AuthToken) Load()
    {
        if (!File.Exists(_configPath))
            return (null, null);

        try
        {
            string json = File.ReadAllText(_configPath);
            var model = JsonSerializer.Deserialize<TwilioConfigModel>(json, JsonOptions);
            return (model?.AccountSid, model?.AuthToken);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Save Account SID and Auth Token to config file. Creates folder if needed.</summary>
    public void Save(string accountSid, string authToken)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var model = new TwilioConfigModel
        {
            AccountSid = accountSid ?? string.Empty,
            AuthToken = authToken ?? string.Empty
        };
        string json = JsonSerializer.Serialize(model, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private sealed class TwilioConfigModel
    {
        public string AccountSid { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
    }
}
