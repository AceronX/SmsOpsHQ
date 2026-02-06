using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Desktop;

public partial class MainWindow : Window
{
    private readonly LoginResult _loginResult;

    public MainWindow(LoginResult loginResult)
    {
        InitializeComponent();

        _loginResult = loginResult;

        Title = $"SmsOps HQ \u2013 {_loginResult.User.Username}";
        UserInfoText.Text = _loginResult.User.FullName;
        RoleText.Text = _loginResult.User.Role;
        ApiUrlText.Text = $"API: {App.ApiClient.BaseUrl}";

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckApiConnectionAsync();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        App.ApiClient.ClearAuthToken();

        LoginWindow loginWindow = new LoginWindow();
        Application.Current.MainWindow = loginWindow;
        loginWindow.Show();
        Close();
    }

    private async Task CheckApiConnectionAsync()
    {
        try
        {
            HttpResponseMessage response = await App.ApiClient.Http.GetAsync("/health");

            if (response.IsSuccessStatusCode)
            {
                StatusText.Text = "Connected to API";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                int statusCode = (int)response.StatusCode;
                StatusText.Text = $"API returned status {statusCode}";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            }
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "Cannot reach API - is it running?";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
    }
}
