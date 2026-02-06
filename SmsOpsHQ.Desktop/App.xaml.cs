using System.Windows;
using Microsoft.Extensions.Configuration;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop;

public partial class App : Application
{
    // Application-wide ApiClient instance. Available after OnStartup completes.
    public static ApiClient ApiClient { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        string apiBaseUrl = configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException(
                "Missing 'ApiBaseUrl' in appsettings.json. "
                + "Example: { \"ApiBaseUrl\": \"http://localhost:5000\" }");

        ApiClient = new ApiClient(apiBaseUrl);

        // Show the login window first. MainWindow is opened after successful login.
        LoginWindow loginWindow = new LoginWindow();
        loginWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ApiClient?.Dispose();
        base.OnExit(e);
    }
}
