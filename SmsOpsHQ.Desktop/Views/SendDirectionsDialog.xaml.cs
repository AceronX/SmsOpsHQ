using System.Text.Json;
using System.Windows;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.Views;

public partial class SendDirectionsDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action? _onSent;
    private readonly int? _threadId;
    private string _directionsUrl = string.Empty;

    public SendDirectionsDialog(Action? onSent = null, string? prefillPhone = null, int? threadId = null)
    {
        InitializeComponent();

        _apiClient = App.ApiClient;
        _appState = App.AppState;
        _onSent = onSent;
        _threadId = threadId;

        if (!string.IsNullOrWhiteSpace(prefillPhone))
            PhoneBox.Text = prefillPhone;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_appState.CurrentStoreId <= 0)
        {
            DestinationPreview.Text = "—";
            ShowError("Select a store before sending directions.");
            SendButton.IsEnabled = false;
            return;
        }

        try
        {
            JsonElement store = await _apiClient.GetStoreAsync(_appState.CurrentStoreId);
            string dest = BuildDestinationLine(store);
            if (string.IsNullOrWhiteSpace(dest))
            {
                DestinationPreview.Text = "—";
                ShowError("Store has no address on file. Add address in Settings (store) or database.");
                SendButton.IsEnabled = false;
                return;
            }

            DestinationPreview.Text = dest;
            _directionsUrl = BuildGoogleDirectionsUrl(dest);
            ErrorBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DestinationPreview.Text = "—";
            ShowError($"Could not load store: {ex.Message}");
            SendButton.IsEnabled = false;
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

        if (string.IsNullOrWhiteSpace(_directionsUrl))
        {
            ShowError("Directions link is not ready.");
            return;
        }

        SendButton.IsEnabled = false;
        StatusText.Text = "Sending…";
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            string storeName = string.IsNullOrWhiteSpace(_appState.CurrentStoreName)
                ? "Our store"
                : _appState.CurrentStoreName;
            string body = $"{storeName} — driving directions:\n{_directionsUrl}";

            var request = new SendMessageRequest
            {
                StoreId = _appState.CurrentStoreId,
                ToPhone = phone,
                Body = body,
                ThreadId = _threadId,
                TwilioNumberId = _appState.CurrentTwilioNumberId
            };

            JsonElement response = await _apiClient.SendMessageAsync(request);

            bool mock = response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("mock", out JsonElement mockEl)
                && mockEl.ValueKind == JsonValueKind.True;

            if (mock)
            {
                ShowError(
                    "MOCK MODE: Twilio is not configured, so the customer did NOT receive this message. " +
                    "Open Settings → Twilio to enter your Account SID and Auth Token, then try again.");
                StatusText.Text = string.Empty;
                return;
            }

            StatusText.Text = "Directions sent.";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");

            _onSent?.Invoke();

            await System.Threading.Tasks.Task.Delay(1200);
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
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(_directionsUrl)
                                   && _appState.CurrentStoreId > 0;
        }
    }

    private static string BuildDestinationLine(JsonElement store)
    {
        static string? S(JsonElement el, string snake, string camel)
        {
            if (el.TryGetProperty(snake, out JsonElement p))
                return p.GetString();
            if (el.TryGetProperty(camel, out p))
                return p.GetString();
            return null;
        }

        string? name = S(store, "store_name", "storeName");
        string? address = S(store, "address", "address");
        string? city = S(store, "city", "city");
        string? state = S(store, "state", "state");
        string? zip = S(store, "zip", "zip");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address))
            parts.Add(address.Trim());

        string cityLine = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(zip))
        {
            cityLine = string.IsNullOrWhiteSpace(cityLine)
                ? zip.Trim()
                : $"{cityLine} {zip}".Trim();
        }

        if (!string.IsNullOrWhiteSpace(cityLine))
            parts.Add(cityLine.Trim());

        string line = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(line) ? (name?.Trim() ?? "") : line;
    }

    private static string BuildGoogleDirectionsUrl(string destination)
    {
        return "https://www.google.com/maps/dir/?api=1&destination="
               + Uri.EscapeDataString(destination);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
