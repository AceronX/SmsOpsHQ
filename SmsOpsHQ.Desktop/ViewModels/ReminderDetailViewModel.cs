using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

/// <summary>Reminder detail screen: shows one sent reminder and customer context panel.</summary>
public sealed partial class ReminderDetailViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly XBlueService? _xblueService;

    [ObservableProperty]
    private int _reminderId;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _customerName = "Unknown";

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private DateTime? _sentAt;

    [ObservableProperty]
    private int? _transNo;

    [ObservableProperty]
    private string? _dueDate;

    [ObservableProperty]
    private string _reminderType = string.Empty;

    [ObservableProperty]
    private string _sentAtText = string.Empty;

    [ObservableProperty]
    private object? _customerPanel;

    public ReminderDetailViewModel(
        ApiClient apiClient,
        AppState appState,
        NavigationService navigation,
        int reminderId,
        string phone,
        string customerName,
        string message,
        DateTime? sentAt,
        int? transNo,
        string? dueDate,
        string reminderType,
        XBlueService? xblueService = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _navigation = navigation;
        _xblueService = xblueService;
        ReminderId = reminderId;
        Phone = phone;
        CustomerName = customerName;
        Message = message;
        SentAt = sentAt;
        TransNo = transNo;
        DueDate = dueDate;
        ReminderType = reminderType;
        SentAtText = FormatSentAt(sentAt);
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigation.NavigateTo<RemindersViewModel>();
    }

    [RelayCommand]
    private async Task LoadCustomerContextAsync()
    {
        if (string.IsNullOrWhiteSpace(Phone))
        {
            CustomerPanel = null;
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            JsonElement response = await _apiClient.GetCustomerByPhoneAsync(Phone);

            if (!response.TryGetProperty("found", out JsonElement foundE) || !foundE.GetBoolean())
            {
                // Still show a minimal panel with what we have from the reminder
                var minimal = new CustomerPanelViewModel(_apiClient, _xblueService);
                minimal.CustomerName = CustomerName;
                minimal.CustomerPhone = Phone;
                minimal.CustomerAddress = "Customer not found by phone.";
                CustomerPanel = minimal;
                return;
            }

            int? appCustomerId = null;
            if (response.TryGetProperty("customer_id", out JsonElement cidE) && cidE.ValueKind == JsonValueKind.Number)
                appCustomerId = cidE.GetInt32();

            var panel = new CustomerPanelViewModel(_apiClient, _xblueService);

            if (appCustomerId.HasValue && appCustomerId.Value > 0)
            {
                panel.CustomerId = appCustomerId.Value;
                CustomerPanel = panel;
                // LoadContextCommand will run when CustomerPanelView loads
            }
            else
            {
                panel.PopulateFromByPhoneResponse(response);
                CustomerPanel = panel;
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to load customer: {ex.Message}");
            var fallback = new CustomerPanelViewModel(_apiClient, _xblueService);
            fallback.CustomerName = CustomerName;
            fallback.CustomerPhone = Phone;
            fallback.CustomerAddress = "Could not load context.";
            CustomerPanel = fallback;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatSentAt(DateTime? utcOrLocal)
    {
        if (!utcOrLocal.HasValue) return "";
        DateTime dt = utcOrLocal.Value.Kind == DateTimeKind.Utc ? utcOrLocal.Value.ToLocalTime() : utcOrLocal.Value;
        return dt.ToString("MMM d, yyyy h:mm tt");
    }
}
