using System.Text.Json;
using System.Windows;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.Views;

public partial class AskForReviewDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action? _onSent;

    public AskForReviewDialog(Action? onSent = null, string? prefillPhone = null)
    {
        InitializeComponent();

        _apiClient = App.ApiClient;
        _appState = App.AppState;
        _onSent = onSent;

        if (!string.IsNullOrWhiteSpace(prefillPhone))
            PhoneBox.Text = prefillPhone;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        string phone = PhoneBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            ShowError("Please enter a phone number.");
            return;
        }

        SendButton.IsEnabled = false;
        StatusText.Text = "Sending review request...";
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            JsonElement result = await _apiClient.SendReviewRequestAsync(
                _appState.CurrentStoreId, phone);

            string platform = result.TryGetProperty("platformName", out var pn)
                ? pn.GetString() ?? "" : "";
            string status = result.TryGetProperty("status", out var st)
                ? st.GetString() ?? "" : "";

            StatusText.Text = $"Review request sent via {platform}. Status: {status}";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");

            _onSent?.Invoke();

            await System.Threading.Tasks.Task.Delay(1500);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = string.Empty;
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
