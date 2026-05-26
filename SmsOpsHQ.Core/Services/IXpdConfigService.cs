namespace SmsOpsHQ.Core.Services;

// Effective XPD connection configuration: where the .XPD file lives, the
// workgroup file, and the credentials needed by the VBScript exporter.
//
// "Effective" means: values from %AppData%\SmsOpsHQ\xpd_config.json overlay
// the values in appsettings.json. Per-field fallback so partial overrides
// (e.g. operator only sets the database path) still work.
public interface IXpdConfigService
{
    XpdConfig GetEffective();

    Task SaveAsync(XpdConfig config, CancellationToken cancellationToken = default);

    bool ConfigFileExists { get; }

    string ConfigFilePath { get; }
}

public sealed class XpdConfig
{
    public string DatabasePath { get; set; } = string.Empty;
    public string MdwPath { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
