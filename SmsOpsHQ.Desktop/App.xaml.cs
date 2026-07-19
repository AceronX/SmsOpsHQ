using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.ViewModels;
using SmsOpsHQ.Desktop.Views;

namespace SmsOpsHQ.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private LocalApiHost? _localApiHost;

    // Application-level singletons accessible by legacy code if needed.
    public static ApiClient ApiClient { get; private set; } = null!;
    public static AppState AppState { get; private set; } = null!;

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

        IConfigurationSection localApiSection = configuration.GetSection("LocalApi");
        if (bool.TryParse(localApiSection["AutoStart"], out bool autoStartLocalApi) && autoStartLocalApi
            && LocalApiHost.IsLocalHostUrl(apiBaseUrl))
        {
            string bundledApi = LocalApiHost.ResolveBundledApiExecutablePath(configuration);
            if (!File.Exists(bundledApi))
            {
                MessageBox.Show(
                    "LocalApi:AutoStart is true but the API executable was not found:\n"
                    + bundledApi
                    + "\n\nCopy SmsOpsHQ.Api into the api subfolder next to this app, or run publish-store.ps1.",
                    "SmsOps HQ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                try
                {
                    _localApiHost = LocalApiHost.TryStartIfConfiguredAsync(configuration, apiBaseUrl)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Could not start the local API in the background:\n" + ex.Message,
                        "SmsOps HQ",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        // Build DI container.
        ServiceCollection services = new();
        ConfigureServices(services, configuration, apiBaseUrl);
        _serviceProvider = services.BuildServiceProvider();

        ApiClient = _serviceProvider.GetRequiredService<ApiClient>();
        AppState = _serviceProvider.GetRequiredService<AppState>();

        XBlueSettings xblue = _serviceProvider.GetRequiredService<XBlueConfigService>().Load();
        _serviceProvider.GetRequiredService<XBlueService>().Configure(
            xblue.Ip,
            xblue.Enabled,
            xblue.Username,
            xblue.Password,
            xblue.SpeakerBeforeDial,
            xblue.OutboundPrefix,
            xblue.PressPoundToSend);

        ShowLogin();
    }

    private static void ConfigureServices(ServiceCollection services, IConfiguration configuration, string apiBaseUrl)
    {
        // Singletons shared across the lifetime of the application.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new ApiClient(apiBaseUrl));
        services.AddSingleton<AppState>();
        services.AddSingleton<CacheService>();
        services.AddSingleton<TwilioConfigService>();
        services.AddSingleton<HubConfigService>();
        services.AddSingleton<XpdSyncSchedulerConfigService>();
        services.AddSingleton<XBlueConfigService>();
        services.AddSingleton<LateCustomersQueryService>();
        services.AddSingleton<CustomerQualityQueryService>();
        services.AddSingleton<XBlueService>();
        services.AddSingleton<IPhonePickerService, PhonePickerService>();
        services.AddSingleton(sp =>
        {
            AppState appState = sp.GetRequiredService<AppState>();
            return new SignalRClient(apiBaseUrl, appState);
        });
        services.AddSingleton<ISendSmsDialogService, SendSmsDialogService>();

        // Navigation service: resolves ViewModels from the DI container.
        services.AddSingleton<NavigationService>(sp =>
        {
            return new NavigationService(type => (ViewModelBase)sp.GetRequiredService(type));
        });

        // ViewModels registered as transient (new instance per navigation).
        services.AddTransient<InboxViewModel>(sp => new InboxViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<NavigationService>(),
            sp.GetRequiredService<SignalRClient>(),
            sp.GetRequiredService<XBlueService>(),
            sp.GetRequiredService<ISendSmsDialogService>(),
            sp.GetRequiredService<CustomerQualityQueryService>(),
            sp.GetRequiredService<IPhonePickerService>()));

        services.AddTransient<TemplatesViewModel>(sp => new TemplatesViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AppState>()));

        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<TwilioConfigService>(),
            sp.GetRequiredService<XBlueService>(),
            sp.GetRequiredService<XBlueConfigService>(),
            sp.GetRequiredService<CustomerQualityQueryService>(),
            sp.GetRequiredService<HubConfigService>(),
            sp.GetRequiredService<XpdSyncSchedulerConfigService>()));

        services.AddTransient<LateCustomersViewModel>(sp => new LateCustomersViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<LateCustomersQueryService>(),
            sp.GetRequiredService<XBlueService>(),
            sp.GetRequiredService<ISendSmsDialogService>(),
            sp.GetRequiredService<CustomerQualityQueryService>(),
            sp.GetRequiredService<IPhonePickerService>()));

        services.AddTransient<RemindersViewModel>(sp => new RemindersViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<NavigationService>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<XBlueService>(),
            sp.GetRequiredService<ISendSmsDialogService>(),
            sp.GetRequiredService<CustomerQualityQueryService>()));

        services.AddTransient<ComposeViewModel>(sp => new ComposeViewModel(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<AppState>()));
    }

    private void ShowLogin()
    {
        if (_serviceProvider is null) return;

        ApiClient apiClient = _serviceProvider.GetRequiredService<ApiClient>();
        AppState appState = _serviceProvider.GetRequiredService<AppState>();

        LoginViewModel loginVm = new(apiClient, appState, OnLoginSuccess);

        LoginWindow loginWindow = new()
        {
            DataContext = loginVm
        };

        MainWindow = loginWindow;
        loginWindow.Show();
    }

    private async void OnLoginSuccess(LoginResult loginResult)
    {
        if (_serviceProvider is null) return;

        NavigationService navigation = _serviceProvider.GetRequiredService<NavigationService>();
        AppState appState = _serviceProvider.GetRequiredService<AppState>();
        ApiClient apiClient = _serviceProvider.GetRequiredService<ApiClient>();
        SignalRClient signalRClient = _serviceProvider.GetRequiredService<SignalRClient>();

        MainViewModel mainVm = new(navigation, appState, signalRClient, OnLogout);

        MainWindow newMain = new()
        {
            DataContext = mainVm
        };

        Window? oldWindow = MainWindow;
        MainWindow = newMain;
        newMain.Show();
        oldWindow?.Close();

        // Initialize after window is shown so the UI is responsive immediately.
        _ = mainVm.InitializeAsync();
    }

    private void OnLogout()
    {
        if (_serviceProvider is null) return;

        ApiClient apiClient = _serviceProvider.GetRequiredService<ApiClient>();
        apiClient.ClearAuthToken();

        _serviceProvider.GetRequiredService<CacheService>().Clear();

        ShowLogin();
        // Close old main window.
        foreach (Window window in Windows)
        {
            if (window is MainWindow && window != MainWindow)
                window.Close();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Order matters here. We must tell the bundled API to gracefully close
        // its Hub SignalR connection BEFORE we hard-kill the API process via
        // _localApiHost.Dispose(). Otherwise the Hub server only learns the
        // store is gone via the SignalR keepalive timeout (~15-30s), and the
        // dashboard's "live" badge and Status pill lag behind reality.
        //
        // The shutdown call has its own 3s timeout and swallows all errors,
        // so this path can never hang or throw on the way out.
        ApiClient? apiClient = _serviceProvider?.GetService<ApiClient>();
        if (apiClient is not null)
        {
            try
            {
                apiClient.ShutdownHubAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Defensive: ShutdownHubAsync already swallows internally, but
                // we still don't want a stray exception to short-circuit the
                // rest of OnExit and leak a child API process.
            }
        }

        _serviceProvider?.GetService<SignalRClient>()?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        apiClient?.Dispose();
        _serviceProvider?.GetService<XBlueService>()?.Dispose();
        _serviceProvider?.Dispose();
        _localApiHost?.Dispose();
        base.OnExit(e);
    }
}
