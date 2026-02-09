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

    public LoginViewModel(ApiClient apiClient, AppState appState, Action<LoginResult> onLoginSuccess)
    {
        _apiClient = apiClient;
        _appState = appState;
        _onLoginSuccess = onLoginSuccess;
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
            SetError("Cannot reach the API server. Is it running?");
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
