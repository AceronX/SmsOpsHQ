using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        UsernameBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(sender, e);
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text.Trim();
        string password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Please enter your username.");
            UsernameBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter your password.");
            PasswordBox.Focus();
            return;
        }

        SetFormEnabled(false);
        HideError();

        try
        {
            LoginRequest loginRequest = new LoginRequest
            {
                Username = username,
                Password = password
            };

            HttpResponseMessage response = await App.ApiClient.Http.PostAsJsonAsync(
                "/api/auth/login",
                loginRequest,
                ApiClient.JsonOptions);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                LoginResult? loginResult = await response.Content.ReadFromJsonAsync<LoginResult>(
                    ApiClient.JsonOptions);

                if (loginResult is null || string.IsNullOrEmpty(loginResult.AccessToken))
                {
                    ShowError("Received an invalid response from the server.");
                    SetFormEnabled(true);
                    return;
                }

                App.ApiClient.SetAuthToken(loginResult.AccessToken);

                MainWindow mainWindow = new MainWindow(loginResult);
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                Close();
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ShowError("Invalid username or password.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                SetFormEnabled(true);
            }
            else
            {
                int statusCode = (int)response.StatusCode;
                ShowError($"Server returned an unexpected status ({statusCode}). Please try again.");
                SetFormEnabled(true);
            }
        }
        catch (HttpRequestException)
        {
            ShowError("Cannot reach the API server. Is it running?");
            SetFormEnabled(true);
        }
        catch (Exception ex)
        {
            ShowError($"An unexpected error occurred: {ex.Message}");
            SetFormEnabled(true);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SetFormEnabled(bool isEnabled)
    {
        UsernameBox.IsEnabled = isEnabled;
        PasswordBox.IsEnabled = isEnabled;
        LoginButton.IsEnabled = isEnabled;
        LoginButton.Content = isEnabled ? "Sign In" : "Signing in...";
    }
}
