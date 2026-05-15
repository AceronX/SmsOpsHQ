using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace SmsOpsHQ.Desktop.Services;

/// <summary>
/// Starts a bundled SmsOpsHQ.Api.exe with no console window when LocalApi:AutoStart is true
/// and ApiBaseUrl points at localhost. Stops the process when the desktop app exits.
/// </summary>
public sealed class LocalApiHost : IDisposable
{
    private readonly Process? _process;
    private readonly bool _startedByUs;

    private LocalApiHost(Process? process, bool startedByUs)
    {
        _process = process;
        _startedByUs = startedByUs;
    }

    public static async Task<LocalApiHost?> TryStartIfConfiguredAsync(
        IConfiguration configuration,
        string apiBaseUrl,
        CancellationToken cancellationToken = default)
    {
        IConfigurationSection section = configuration.GetSection("LocalApi");
        if (!bool.TryParse(section["AutoStart"], out bool autoStart) || !autoStart)
            return null;

        if (!IsLocalHostUrl(apiBaseUrl))
            return null;

        string relative = section["ExecutableRelativePath"] ?? Path.Combine("api", "SmsOpsHQ.Api.exe");
        string exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
        string workDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        if (!File.Exists(exePath))
            return null;

        if (await IsHealthyAsync(apiBaseUrl, cancellationToken).ConfigureAwait(false))
            return new LocalApiHost(null, false);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start local API process.");

        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (await IsHealthyAsync(apiBaseUrl, cancellationToken).ConfigureAwait(false))
                return new LocalApiHost(process, true);

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Local API exited before it became ready. Check logs in the api folder (logs/smsops-*.log).");
            }
        }

        try
        {
            process.Kill(true);
        }
        catch
        {
            // ignore
        }

        throw new InvalidOperationException(
            "Local API did not respond on /health in time. Another program may be using port 5000.");
    }

    public static bool IsLocalHostUrl(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
            || uri.Host == "::1";
    }

    public static string ResolveBundledApiExecutablePath(IConfiguration configuration)
    {
        string relative = configuration.GetSection("LocalApi")["ExecutableRelativePath"]
            ?? Path.Combine("api", "SmsOpsHQ.Api.exe");
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
    }

    private static async Task<bool> IsHealthyAsync(string apiBaseUrl, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/health";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_startedByUs || _process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(true);
        }
        catch
        {
            // ignore
        }

        _process.Dispose();
    }
}
