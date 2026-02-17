using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents a single thread in the inbox list.
public sealed class InboxThreadItem
{
    public int ThreadId { get; set; }
    public string CustomerName { get; set; } = "Unknown";
    /// <summary>Display name formatted as "Last, First" (or first/last only).</summary>
    public string DisplayName { get; set; } = "Unknown";
    public string CustomerPhone { get; set; } = string.Empty;
    public string LastMessageBody { get; set; } = string.Empty;
    public string LastMessageDirection { get; set; } = string.Empty;
    public string LastMessageTime { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public string Status { get; set; } = "Open";
    public int? CustomerId { get; set; }
    /// <summary>First letter for avatar (from DisplayName or Phone).</summary>
    public string AvatarLetter { get; set; } = "?";
}

// Inbox ViewModel: loads threads, supports search and filter.
public sealed partial class InboxViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly SignalRClient _signalRClient;
    private readonly XBlueService? _xblueService;

    [ObservableProperty]
    private ObservableCollection<InboxThreadItem> _threads = new();

    [ObservableProperty]
    private InboxThreadItem? _selectedThread;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "open";

    [ObservableProperty]
    private int _totalThreads;

    public InboxViewModel(ApiClient apiClient, AppState appState, NavigationService navigation, SignalRClient signalRClient, XBlueService? xblueService = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _navigation = navigation;
        _signalRClient = signalRClient;
        _xblueService = xblueService;

        _signalRClient.MessageReceived += OnSignalRMessageReceived;
    }

    // Reload inbox when a real-time message arrives.
    private void OnSignalRMessageReceived(JsonElement message, JsonElement thread)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => LoadInboxCommand.ExecuteAsync(null));
    }

    partial void OnSelectedThreadChanged(InboxThreadItem? value)
    {
        if (value is not null)
            OpenThread(value);
    }

    [RelayCommand]
    private async Task LoadInboxAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
            JsonElement result = await _apiClient.GetInboxAsync(_appState.CurrentStoreId, SelectedFilter, search);

            ObservableCollection<InboxThreadItem> newThreads = new();

            foreach (JsonElement threadJson in result.EnumerateArray())
            {
                InboxThreadItem item = new()
                {
                    ThreadId = threadJson.GetProperty("thread_id").GetInt32(),
                    UnreadCount = threadJson.GetProperty("unread_count").GetInt32(),
                    Status = threadJson.GetProperty("status").GetString() ?? "Open"
                };

                if (threadJson.TryGetProperty("last_message_at", out JsonElement lmaElem) && lmaElem.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(lmaElem.GetString(), out DateTime lma))
                        item.LastMessageTime = FormatInboxTime(lma);
                }

                if (threadJson.TryGetProperty("customer", out JsonElement custElem) && custElem.ValueKind == JsonValueKind.Object)
                {
                    string name = custElem.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() ?? "" : "";
                    item.CustomerName = string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
                    item.CustomerPhone = custElem.TryGetProperty("phone", out JsonElement phoneElem) ? phoneElem.GetString() ?? "" : "";
                    if (custElem.TryGetProperty("id", out JsonElement idElem) && idElem.ValueKind == JsonValueKind.Number)
                        item.CustomerId = idElem.GetInt32();
                    string? first = custElem.TryGetProperty("first_name", out JsonElement fn) ? fn.GetString() : null;
                    string? last = custElem.TryGetProperty("last_name", out JsonElement ln) ? ln.GetString() : null;
                    item.DisplayName = FormatDisplayName(first, last, item.CustomerName);
                    item.AvatarLetter = GetAvatarLetter(item.DisplayName, item.CustomerPhone);
                }
                else
                {
                    item.DisplayName = item.CustomerName;
                    item.AvatarLetter = GetAvatarLetter(item.DisplayName, item.CustomerPhone);
                }

                if (threadJson.TryGetProperty("last_message", out JsonElement lmElem) && lmElem.ValueKind == JsonValueKind.Object)
                {
                    item.LastMessageBody = lmElem.TryGetProperty("body", out JsonElement bodyElem) ? bodyElem.GetString() ?? "" : "";
                    item.LastMessageDirection = lmElem.TryGetProperty("direction", out JsonElement dirElem) ? dirElem.GetString() ?? "" : "";
                }

                newThreads.Add(item);
            }

            Threads = newThreads;
            TotalThreads = newThreads.Count;
        }
        catch (Exception ex)
        {
            SetError($"Failed to load inbox: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadInboxAsync();
    }

    [RelayCommand]
    private async Task SetFilterAsync(string filter)
    {
        SelectedFilter = filter;
        await LoadInboxAsync();
    }

    private void OpenThread(InboxThreadItem item)
    {
        ThreadViewModel threadVm = new(_apiClient, _appState, _navigation, _signalRClient, item.ThreadId, item.CustomerName, _xblueService);
        if (item.CustomerId.HasValue)
            threadVm.CustomerId = item.CustomerId;
        _navigation.NavigateTo(threadVm);
    }

    /// <summary>Today: "h:mm tt"; this week: "ddd h:mm tt"; older: "MM/dd h:mm tt".</summary>
    private static string FormatInboxTime(DateTime utcOrLocal)
    {
        DateTime dt = utcOrLocal.Kind == DateTimeKind.Utc ? utcOrLocal.ToLocalTime() : utcOrLocal;
        DateTime now = DateTime.Now;
        if (dt.Date == now.Date)
            return dt.ToString("h:mm tt");
        if ((now - dt).TotalDays < 7)
            return dt.ToString("ddd h:mm tt");
        return dt.ToString("MM/dd h:mm tt");
    }

    private static string FormatDisplayName(string? first, string? last, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(first))
            return $"{last.Trim()}, {first.Trim()}";
        if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        if (!string.IsNullOrWhiteSpace(last)) return last.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback;
    }

    private static string GetAvatarLetter(string displayName, string phone)
    {
        string s = string.IsNullOrWhiteSpace(displayName) ? phone : displayName;
        if (string.IsNullOrWhiteSpace(s)) return "?";
        char c = s.Trim()[0];
        if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (char.IsDigit(c)) return c.ToString();
        return "?";
    }
}
