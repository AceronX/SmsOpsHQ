using System.Text.Json;
using System.Windows;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.Views;

public partial class AskForReviewDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action? _onSent;
    private bool _isReady;

    public AskForReviewDialog(Action? onSent = null, string? prefillPhone = null)
    {
        InitializeComponent();

        _apiClient = App.ApiClient;
        _appState = App.AppState;
        _onSent = onSent;

        if (!string.IsNullOrWhiteSpace(prefillPhone))
            PhoneBox.Text = prefillPhone;
    }

    private async void Dialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshReadinessAsync();
    }

    private async Task RefreshReadinessAsync()
    {
        _isReady = false;
        SendButton.IsEnabled = false;
        ReadinessText.Text = "Checking review prerequisites...";
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            ReviewReadinessDto readiness = await _apiClient.GetReviewReadinessAsync(
                _appState.CurrentStoreId,
                _appState.CurrentTwilioNumberId);

            _isReady = readiness.Ready;
            if (readiness.Ready)
            {
                ReadinessText.Text = "Ready — sender, Twilio, channel, and Review template checks passed.";
                ReadinessText.Foreground = (System.Windows.Media.Brush)FindResource("Success");
                SendButton.IsEnabled = true;
                return;
            }

            List<string> failures = readiness.Checks
                .Where(check => !check.Passed)
                .Select(check => $"• {check.Label}: {check.Message}")
                .ToList();
            string message = failures.Count == 0
                ? "Review sending is not ready."
                : string.Join(Environment.NewLine, failures);
            ReadinessText.Text = "Blocked";
            ReadinessText.Foreground = (System.Windows.Media.Brush)FindResource("Error");
            ShowError(message);
        }
        catch (Exception ex)
        {
            ReadinessText.Text = "Readiness check unavailable";
            ReadinessText.Foreground = (System.Windows.Media.Brush)FindResource("Error");
            ShowError(ex.Message);
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        string phone = PhoneBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            ShowError("Please enter a phone number.");
            return;
        }

        if (!_isReady)
        {
            ShowError("Review sending prerequisites have not passed.");
            return;
        }

        SendButton.IsEnabled = false;
        StatusText.Text = "Sending review request...";
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            JsonElement result = await _apiClient.SendReviewRequestAsync(
                _appState.CurrentStoreId,
                phone,
                _appState.CurrentTwilioNumberId);

            string status = result.TryGetProperty("status", out JsonElement statusElement)
                ? statusElement.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                ShowError($"Review request was not accepted. Status: {status}");
                StatusText.Text = string.Empty;
                return;
            }

            StatusText.Text = "Review request accepted by Twilio.";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");
            _onSent?.Invoke();

            await Task.Delay(1500);
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
            if (IsVisible)
                SendButton.IsEnabled = _isReady;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
