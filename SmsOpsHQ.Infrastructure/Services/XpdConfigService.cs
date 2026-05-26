using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Infrastructure.Services;

// Loads + persists XPD connection settings to %AppData%\SmsOpsHQ\xpd_config.json.
// Falls back per-field to appsettings.json (Xpd:DatabasePath, Xpd:MdwPath,
// Xpd:User, Xpd:Password) so the file can be partial. Same shape and behavior
// as TwilioConfigService on the Desktop side, but here on the API side so
// both manual and scheduled syncs read from the same source of truth.
public sealed class XpdConfigService : IXpdConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<XpdConfigService> _logger;
    private readonly string _configPath;
    private readonly object _lock = new();

    public XpdConfigService(IConfiguration configuration, ILogger<XpdConfigService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configPath = Path.Combine(appData, "SmsOpsHQ", "xpd_config.json");
    }

    public string ConfigFilePath => _configPath;

    public bool ConfigFileExists => File.Exists(_configPath);

    public XpdConfig GetEffective()
    {
        XpdConfig overlay = LoadOverlay();
        IConfigurationSection xpd = _configuration.GetSection("Xpd");

        return new XpdConfig
        {
            DatabasePath = !string.IsNullOrWhiteSpace(overlay.DatabasePath)
                ? overlay.DatabasePath
                : xpd["DatabasePath"] ?? @"C:\xpawndata\pitkin.XPD",
            MdwPath = !string.IsNullOrWhiteSpace(overlay.MdwPath)
                ? overlay.MdwPath
                : xpd["MdwPath"] ?? @"C:\xpawndata\XcelData.mdw",
            User = !string.IsNullOrWhiteSpace(overlay.User)
                ? overlay.User
                : xpd["User"] ?? "developer",
            Password = !string.IsNullOrWhiteSpace(overlay.Password)
                ? overlay.Password
                : xpd["Password"] ?? "Hollerith89"
        };
    }

    public async Task SaveAsync(XpdConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        XpdConfig existing = LoadOverlay();

        // Merge: only overwrite fields the caller explicitly provided. An empty
        // string means "leave the previous overlay value alone" so partial saves
        // (e.g. updating just the password) don't blow away other fields.
        if (!string.IsNullOrWhiteSpace(config.DatabasePath))
            existing.DatabasePath = config.DatabasePath.Trim();
        if (!string.IsNullOrWhiteSpace(config.MdwPath))
            existing.MdwPath = config.MdwPath.Trim();
        if (!string.IsNullOrWhiteSpace(config.User))
            existing.User = config.User.Trim();
        if (!string.IsNullOrWhiteSpace(config.Password))
            existing.Password = config.Password;

        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(existing, JsonOptions);
        string tempPath = _configPath + ".tmp";

        // Atomic write: temp file then rename so a mid-write crash never
        // leaves a corrupt JSON file the API would then fail to parse.
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        lock (_lock)
        {
            if (File.Exists(_configPath))
                File.Replace(tempPath, _configPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _configPath);
        }

        _logger.LogInformation("Persisted XPD config to {Path}", _configPath);
    }

    private XpdConfig LoadOverlay()
    {
        lock (_lock)
        {
            if (!File.Exists(_configPath))
                return new XpdConfig();

            try
            {
                string json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<XpdConfig>(json, JsonOptions)
                    ?? new XpdConfig();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to read XPD config from {Path}; falling back to appsettings.",
                    _configPath);
                return new XpdConfig();
            }
        }
    }
}
