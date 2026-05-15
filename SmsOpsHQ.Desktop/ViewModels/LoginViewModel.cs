using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// ViewModel for the login screen. Handles credential input, validation,
// API login, token storage, and navigation to the main window.
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action<LoginResult> _onLoginSuccess;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>Configured API root from appsettings.json (shown on login for store troubleshooting).</summary>
    public string ApiBaseUrlDisplay => (_apiClient.BaseUrl ?? string.Empty).TrimEnd('/');

    public LoginViewModel(ApiClient apiClient, AppState appState, Action<LoginResult> onLoginSuccess)
    {
        _apiClient = apiClient;
        _appState = appState;
        _onLoginSuccess = onLoginSuccess;
    }

    private string ApiUnreachableMessage(string suffix = "")
    {
        string url = ApiBaseUrlDisplay;
        if (string.IsNullOrEmpty(url))
            url = "(not set)";

        return $"Cannot reach the API at {url}.{suffix} "
            + "Start SmsOpsHQ.Api where that URL points (or use the correct host/IP). "
            + "On this PC, set ApiBaseUrl in appsettings.json next to SmsOpsHQ.Desktop.exe, then restart the app.";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ClearError();

        if (string.IsNullOrWhiteSpace(Username))
        {
            SetError("Please enter your username.");
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            SetError("Please enter your password.");
            return;
        }

        IsBusy = true;

        try
        {
            LoginResult? result = await _apiClient.LoginAsync(Username, Password);

            if (result is null || string.IsNullOrEmpty(result.AccessToken))
            {
                SetError("Invalid username or password.");
                Password = string.Empty;
                IsBusy = false;
                return;
            }

            _apiClient.SetAuthToken(result.AccessToken);
            _appState.SetLoginState(result);
            _onLoginSuccess(result);
        }
        catch (HttpRequestException)
        {
            SetError(ApiUnreachableMessage());
        }
        catch (TaskCanceledException)
        {
            SetError(ApiUnreachableMessage(" Request timed out."));
        }
        catch (Exception ex)
        {
            SetError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
