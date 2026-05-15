using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SmsOpsHQ.Desktop.Services;

public readonly record struct XBlueSettings(
    string Ip,
    bool Enabled,
    string Username,
    string Password,
    bool SpeakerBeforeDial,
    string OutboundPrefix,
    bool PressPoundToSend);

// Persists XBlue / Fanvil click-to-call settings under AppData (overrides appsettings defaults).
public sealed class XBlueConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfiguration _configuration;
    private readonly string _configPath;

    public XBlueConfigService(IConfiguration configuration)
    {
        _configuration = configuration;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "SmsOpsHQ");
        _configPath = Path.Combine(folder, "xblue_config.json");
    }

    public string ConfigFilePath => _configPath;

    public XBlueSettings Load()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                string json = File.ReadAllText(_configPath);
                XBlueConfigModel? m = JsonSerializer.Deserialize<XBlueConfigModel>(json, JsonOptions);
                if (m is not null)
                {
                    string user = m.Username?.Trim() ?? "";
                    string pass = m.Password ?? "";
                    if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
                    {
                        user = _configuration["XBlue:Username"]?.Trim() ?? "";
                        pass = _configuration["XBlue:Password"] ?? "";
                    }

                    bool speakerFirst = m.SpeakerBeforeDial ?? ReadSpeakerBeforeDialDefault();
                    string prefix = m.OutboundPrefix ?? _configuration["XBlue:OutboundPrefix"] ?? "";
                    bool poundSend = m.PressPoundToSend ?? ReadPressPoundToSendDefault();
                    return new XBlueSettings(
                        m.Ip?.Trim() ?? "",
                        m.Enabled,
                        user,
                        pass,
                        speakerFirst,
                        new string(prefix.Where(char.IsDigit).ToArray()),
                        poundSend);
                }
            }
            catch
            {
                // fall through to appsettings
            }
        }

        string ip = _configuration["XBlue:Ip"]?.Trim() ?? "";
        bool enabled = bool.TryParse(_configuration["XBlue:Enabled"], out bool e) && e;
        string username = _configuration["XBlue:Username"]?.Trim() ?? "";
        string password = _configuration["XBlue:Password"] ?? "";
        bool speakerDial = ReadSpeakerBeforeDialDefault();
        string outboundPrefix = _configuration["XBlue:OutboundPrefix"] ?? "";
        outboundPrefix = new string(outboundPrefix.Where(char.IsDigit).ToArray());
        bool poundDefault = ReadPressPoundToSendDefault();
        return new XBlueSettings(ip, enabled, username, password, speakerDial, outboundPrefix, poundDefault);
    }

    private bool ReadPressPoundToSendDefault()
    {
        return bool.TryParse(_configuration["XBlue:PressPoundToSend"], out bool v) && v;
    }

    private bool ReadSpeakerBeforeDialDefault()
    {
        if (!bool.TryParse(_configuration["XBlue:SpeakerBeforeDial"], out bool v))
            return true;
        return v;
    }

    public void Save(string ip, bool enabled, string username, string password, bool speakerBeforeDial, string outboundPrefix,
        bool pressPoundToSend)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var model = new XBlueConfigModel
        {
            Ip = ip.Trim(),
            Enabled = enabled,
            Username = username.Trim(),
            Password = password,
            SpeakerBeforeDial = speakerBeforeDial,
            OutboundPrefix = new string((outboundPrefix ?? "").Where(char.IsDigit).ToArray()),
            PressPoundToSend = pressPoundToSend
        };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(model, JsonOptions));
    }

    private sealed class XBlueConfigModel
    {
        public string Ip { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool? SpeakerBeforeDial { get; set; }
        public string? OutboundPrefix { get; set; }
        public bool? PressPoundToSend { get; set; }
    }
}
