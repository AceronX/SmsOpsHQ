using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

/// <summary>One row in the sent reminders list.</summary>
public sealed class ReminderListItem
{
    public int ReminderId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Unknown";
    public string Message { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public string ReminderType { get; set; } = string.Empty;
    public int? TransNo { get; set; }
    public string? DueDate { get; set; }
    public string AvatarLetter { get; set; } = "?";
    public string TimeText { get; set; } = string.Empty;
    /// <summary>E.g. "#1234" or reminder type for display on second line.</summary>
    public string TicketOrTypeDisplay => TransNo.HasValue ? $"#{TransNo}" : (ReminderType ?? "");
    /// <summary>Second line: Phone + optional " · #123" or " · Type".</summary>
    public string SubtitleLine => string.IsNullOrEmpty(TicketOrTypeDisplay) ? Phone : $"{Phone} · {TicketOrTypeDisplay}";
}

/// <summary>Reminders list screen: loads sent reminders, navigates to detail on selection.</summary>
public sealed partial class RemindersViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly NavigationService _navigation;
    private readonly AppState _appState;
    private readonly XBlueService? _xblueService;
    private readonly ISendSmsDialogService? _sendSmsDialogService;
    private readonly CustomerQualityQueryService? _qualityQueryService;

    [ObservableProperty]
    private ObservableCollection<ReminderListItem> _reminders = new();

    [ObservableProperty]
    private ObservableCollection<ReminderListItem> _filteredReminders = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ReminderListItem? _selectedReminder;

    [ObservableProperty]
    private ReminderDetailViewModel? _currentReminderDetailViewModel;

    [ObservableProperty]
    private CustomerPanelViewModel? _customerPanel;

    public RemindersViewModel(
        ApiClient apiClient,
        NavigationService navigation,
        AppState appState,
        XBlueService? xblueService,
        ISendSmsDialogService? sendSmsDialogService = null,
        CustomerQualityQueryService? qualityQueryService = null)
    {
        _apiClient = apiClient;
        _navigation = navigation;
        _appState = appState;
        _xblueService = xblueService;
        _sendSmsDialogService = sendSmsDialogService;
        _qualityQueryService = qualityQueryService;
    }

    partial void OnSelectedReminderChanged(ReminderListItem? value)
    {
        if (value is not null)
            OpenReminder(value);
    }

    partial void OnSearchTextChanged(string value) => RefreshFiltered();

    partial void OnRemindersChanged(ObservableCollection<ReminderListItem> value) => RefreshFiltered();

    private void RefreshFiltered()
    {
        FilteredReminders.Clear();
        string term = (SearchText ?? "").Trim().ToUpperInvariant();
        foreach (ReminderListItem item in Reminders)
        {
            if (string.IsNullOrEmpty(term) || MatchesSearch(item, term))
                FilteredReminders.Add(item);
        }
    }

    private static bool MatchesSearch(ReminderListItem item, string term)
    {
        if (item.CustomerName?.ToUpperInvariant().Contains(term) == true) return true;
        if (item.Phone?.ToUpperInvariant().Contains(term) == true) return true;
        if (item.Message?.ToUpperInvariant().Contains(term) == true) return true;
        if (item.ReminderType?.ToUpperInvariant().Contains(term) == true) return true;
        if (item.DueDate?.ToUpperInvariant().Contains(term) == true) return true;
        if (item.TransNo.HasValue && item.TransNo.Value.ToString().Contains(term, StringComparison.Ordinal)) return true;
        return false;
    }

    [RelayCommand]
    private async Task LoadRemindersAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            JsonElement result = await _apiClient.GetSentRemindersAsync(200);
            ObservableCollection<ReminderListItem> items = new();

            foreach (JsonElement r in result.EnumerateArray())
            {
                DateTime? sentAt = null;
                if (r.TryGetProperty("sentAt", out JsonElement sa) && sa.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(sa.GetString(), out DateTime dt))
                        sentAt = dt;
                }
                else if (r.TryGetProperty("sentAt", out JsonElement sa2) && sa2.ValueKind == JsonValueKind.Number)
                {
                    // Unix ms or similar
                    long ms = sa2.GetInt64();
                    if (ms > 0)
                        sentAt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                }

                string customerName = r.TryGetProperty("customerName", out JsonElement cn) ? cn.GetString() ?? "Unknown" : "Unknown";
                string phone = r.TryGetProperty("phone", out JsonElement ph) ? ph.GetString() ?? "" : "";

                items.Add(new ReminderListItem
                {
                    ReminderId = r.TryGetProperty("reminderId", out JsonElement rid) ? rid.GetInt32() : 0,
                    Phone = phone,
                    CustomerName = customerName,
                    Message = r.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "" : "",
                    SentAt = sentAt,
                    ReminderType = r.TryGetProperty("reminderType", out JsonElement rt) ? rt.GetString() ?? "" : "",
                    TransNo = r.TryGetProperty("transNo", out JsonElement tn) && tn.ValueKind != JsonValueKind.Null ? tn.GetInt32() : null,
                    DueDate = r.TryGetProperty("dueDate", out JsonElement dd) ? dd.GetString() : null,
                    AvatarLetter = GetAvatarLetter(customerName, phone),
                    TimeText = FormatReminderTime(sentAt)
                });
            }

            Reminders = items;
        }
        catch (Exception ex)
        {
            SetError($"Failed to load reminders: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenReminder(ReminderListItem item)
    {
        ReminderDetailViewModel detailVm = new(
            _apiClient,
            _appState,
            _navigation,
            item.ReminderId,
            item.Phone,
            item.CustomerName,
            item.Message,
            item.SentAt,
            item.TransNo,
            item.DueDate,
            item.ReminderType,
            _xblueService,
            sendSmsDialogService: _sendSmsDialogService,
            qualityQueryService: _qualityQueryService,
            setRightPanel: p => CustomerPanel = p,
            onCloseRequested: () =>
            {
                CurrentReminderDetailViewModel = null;
                CustomerPanel = null;
            });
        CurrentReminderDetailViewModel = detailVm;
        // Load right-hand customer detail panel immediately so it stays in sync with selection
        _ = detailVm.LoadCustomerContextCommand.ExecuteAsync(null);
    }

    private static string GetAvatarLetter(string name, string phone)
    {
        string s = string.IsNullOrWhiteSpace(name) ? phone : name;
        if (string.IsNullOrWhiteSpace(s)) return "?";
        char c = s.Trim()[0];
        if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (char.IsDigit(c)) return c.ToString();
        return "?";
    }

    private static string FormatReminderTime(DateTime? utcOrLocal)
    {
        if (!utcOrLocal.HasValue) return "";
        DateTime dt = utcOrLocal.Value.Kind == DateTimeKind.Utc ? utcOrLocal.Value.ToLocalTime() : utcOrLocal.Value;
        DateTime now = DateTime.Now;
        if (dt.Date == now.Date)
            return dt.ToString("h:mm tt");
        if ((now - dt).TotalDays < 7)
            return dt.ToString("ddd h:mm tt");
        return dt.ToString("MM/dd h:mm tt");
    }
}
