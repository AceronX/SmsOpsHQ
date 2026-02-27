using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Shell ViewModel: manages sidebar navigation and top bar state.
// The ContentPresenter in MainWindow binds to CurrentView.
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly AppState _appState;
    private readonly SignalRClient _signalRClient;
    private readonly Action _onLogout;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _selectedNavItem = "Inbox";

    public AppState AppState => _appState;

    public MainViewModel(
        NavigationService navigation,
        AppState appState,
        SignalRClient signalRClient,
        Action onLogout)
    {
        _navigation = navigation;
        _appState = appState;
        _signalRClient = signalRClient;
        _onLogout = onLogout;

        _navigation.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NavigationService.CurrentView))
                CurrentView = _navigation.CurrentView;
        };
    }

    [RelayCommand]
    private void Navigate(string section)
    {
        SelectedNavItem = section;
        if (section == "Inbox" && _navigation.CurrentView is InboxViewModel)
            return;
        if (section == "Reminders" && _navigation.CurrentView is RemindersViewModel)
            return;
        switch (section)
        {
            case "Inbox":
                _navigation.NavigateTo<InboxViewModel>();
                break;
            case "Reminders":
                _navigation.NavigateTo<RemindersViewModel>();
                break;
            case "Late":
                _navigation.NavigateTo<LateCustomersViewModel>();
                break;
            case "PFX":
                _navigation.NavigateTo<PfxCustomersViewModel>();
                break;
            case "Templates":
                _navigation.NavigateTo<TemplatesViewModel>();
                break;
            case "Compose":
                _navigation.NavigateTo<ComposeViewModel>();
                break;
            case "Settings":
                _navigation.NavigateTo<SettingsViewModel>();
                break;
        }
    }

    // Sign out: clear state, disconnect SignalR, invoke callback.
    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _signalRClient.DisposeAsync();
        _appState.ClearState();
        _onLogout();
    }

    // Connect SignalR and navigate to default view.
    public async Task InitializeAsync()
    {
        try
        {
            await _signalRClient.ConnectAsync();
        }
        catch
        {
            // SignalR is best-effort; don't block the UI.
        }

        Navigate("Inbox");
    }
}
