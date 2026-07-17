using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

/// <summary>
/// Represents a single thread row in the inbox list.
/// Observable so in-place property updates (SignalR, mark-read) reflect in the UI
/// without replacing the entire collection.
/// </summary>
public sealed partial class InboxThreadItem : ObservableObject
{
    public int ThreadId { get; init; }
    public int? CustomerId { get; set; }

    [ObservableProperty] private string _customerName = "Unknown";
    [ObservableProperty] private string _displayName = "Unknown";
    [ObservableProperty] private string _customerPhone = string.Empty;
    [ObservableProperty] private string _lastMessageBody = string.Empty;
    [ObservableProperty] private string _lastMessageDirection = string.Empty;
    [ObservableProperty] private string _lastMessageTime = string.Empty;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private string _status = "Open";
    [ObservableProperty] private string _avatarLetter = "?";
    [ObservableProperty] private bool _isChecked;

    /// <summary>Raised when <see cref="IsChecked"/> changes so the parent VM can recount.</summary>
    public Action? CheckedChanged { get; set; }

    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke();
}

/// <summary>
/// Inbox ViewModel — loads threads, supports search, filter, and selection-mode deletion.
/// </summary>
public sealed partial class InboxViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly SignalRClient _signalRClient;
    private readonly XBlueService? _xblueService;
    private readonly ISendSmsDialogService _sendSmsDialogService;
    private readonly CustomerQualityQueryService? _qualityQueryService;

    private CancellationTokenSource? _searchDebounce;

    #region Observable Properties

    [ObservableProperty] private ObservableCollection<InboxThreadItem> _threads = new();
    [ObservableProperty] private InboxThreadItem? _selectedThread;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFilter = "open";
    [ObservableProperty] private int _totalThreads;
    [ObservableProperty] private ThreadViewModel? _currentThreadViewModel;
    [ObservableProperty] private CustomerPanelViewModel? _customerPanel;
    [ObservableProperty] private int _checkedCount;
    [ObservableProperty] private bool _isSelectionMode;

    public bool IsFilterAll => SelectedFilter == "all";
    public bool IsFilterOpen => SelectedFilter == "open";
    public bool IsFilterUnread => SelectedFilter == "unread";
    public bool IsFilterClosed => SelectedFilter == "closed";

    #endregion

    public InboxViewModel(
        ApiClient apiClient,
        AppState appState,
        NavigationService navigation,
        SignalRClient signalRClient,
        XBlueService? xblueService,
        ISendSmsDialogService sendSmsDialogService,
        CustomerQualityQueryService? qualityQueryService = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _navigation = navigation;
        _signalRClient = signalRClient;
        _xblueService = xblueService;
        _sendSmsDialogService = sendSmsDialogService;
        _qualityQueryService = qualityQueryService;

        _signalRClient.MessageReceived += OnSignalRMessageReceived;
    }

    #region Property-Changed Hooks

    partial void OnSelectedFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsFilterUnread));
        OnPropertyChanged(nameof(IsFilterClosed));
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var ct = _searchDebounce.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(400, ct); }
            catch (OperationCanceledException) { return; }
            Application.Current?.Dispatcher.Invoke(() => LoadInboxCommand.Execute(null));
        }, ct);
    }

    partial void OnSelectedThreadChanged(InboxThreadItem? value)
    {
        if (value is not null)
            OpenThread(value);
    }

    #endregion

    #region Commands — Inbox

    [RelayCommand]
    private async Task LoadInboxAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
            JsonElement result = await _apiClient.GetInboxAsync(
                _appState.CurrentStoreId,
                SelectedFilter,
                search,
                _appState.CurrentTwilioNumberId);

            var freshItems = new List<InboxThreadItem>();
            foreach (JsonElement json in result.EnumerateArray())
            {
                InboxThreadItem item = ParseThreadItem(json);
                item.CheckedChanged = RefreshCheckedCount;
                freshItems.Add(item);
            }

            MergeInbox(freshItems);
            TotalThreads = Threads.Count;
            RefreshCheckedCount();
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
    private async Task SearchAsync() => await LoadInboxAsync();

    [RelayCommand]
    private async Task SetFilterAsync(string filter)
    {
        SelectedFilter = filter;
        ClearThreadPanel();
        await LoadInboxAsync();
    }

    [RelayCommand]
    private void New()
    {
        ClearThreadPanel();

        _sendSmsDialogService.ShowDialog(
            onSent: () => LoadInboxCommand.Execute(null),
            onPhoneForPreview: phone =>
            {
                if (string.IsNullOrWhiteSpace(phone))
                {
                    CustomerPanel = null;
                    return;
                }
                var panel = new CustomerPanelViewModel(_apiClient, _xblueService, _sendSmsDialogService, _qualityQueryService);
                CustomerPanel = panel;
                _ = panel.LoadByPhoneAsync(phone);
            });
    }

    [RelayCommand]
    private void AskForReview()
    {
        string? prefillPhone = SelectedThread?.CustomerPhone;

        var dialog = new SmsOpsHQ.Desktop.Views.AskForReviewDialog(
            onSent: () => LoadInboxCommand.Execute(null),
            prefillPhone: prefillPhone);

        Window? owner = Application.Current?.MainWindow;
        if (owner is not null)
            dialog.Owner = owner;

        dialog.ShowDialog();
    }

    [RelayCommand]
    private void SendDirections()
    {
        string? prefillPhone = SelectedThread?.CustomerPhone;
        int? threadId = SelectedThread?.ThreadId;

        var dialog = new SmsOpsHQ.Desktop.Views.SendDirectionsDialog(
            onSent: () => LoadInboxCommand.Execute(null),
            prefillPhone: prefillPhone,
            threadId: threadId);

        Window? owner = Application.Current?.MainWindow;
        if (owner is not null)
            dialog.Owner = owner;

        dialog.ShowDialog();
    }

    #endregion

    #region Commands — Selection Mode

    /// <summary>Toggles selection mode so the user can pick threads to delete.</summary>
    [RelayCommand]
    private void Trash()
    {
        if (IsSelectionMode)
            ExitSelectionMode();
        else
            IsSelectionMode = true;
    }

    [RelayCommand]
    private void CancelSelection() => ExitSelectionMode();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in Threads)
            t.IsChecked = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var t in Threads)
            t.IsChecked = false;
    }

    /// <summary>Deletes checked threads, or prompts to delete all when none are checked.</summary>
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Threads.Where(t => t.IsChecked).ToList();

        if (selected.Count == 0)
        {
            if (!ConfirmAction(
                    "No conversations selected.\n" +
                    "Delete ALL conversations for this store?\n\n" +
                    "This cannot be undone.",
                    "Delete All"))
                return;

            await ExecuteDeleteAsync(async () =>
            {
                await _apiClient.DeleteAllConversationsAsync(_appState.CurrentStoreId);
                ClearThreadPanel();
            });
            return;
        }

        string prompt = selected.Count == 1
            ? $"Delete conversation with {selected[0].DisplayName}?\nThis cannot be undone."
            : $"Delete {selected.Count} selected conversations?\nThis cannot be undone.";

        if (!ConfirmAction(prompt, "Delete Conversations"))
            return;

        await ExecuteDeleteAsync(async () =>
        {
            foreach (var item in selected)
            {
                await _apiClient.DeleteThreadAsync(_appState.CurrentStoreId, item.ThreadId);
                DetachIfActiveThread(item.ThreadId);
            }
        });
    }

    #endregion

    #region Thread Panel

    private void OpenThread(InboxThreadItem item)
    {
        CurrentThreadViewModel?.Detach();

        var threadVm = new ThreadViewModel(
            _apiClient, _appState, _navigation, _signalRClient,
            item.ThreadId, item.CustomerName, _xblueService,
            sendSmsDialogService: _sendSmsDialogService,
            qualityQueryService: _qualityQueryService,
            setRightPanel: p => CustomerPanel = p,
            onCloseRequested: () => { CurrentThreadViewModel = null; CustomerPanel = null; },
            onMessagesLoaded: () => { item.UnreadCount = 0; });

        if (item.CustomerId.HasValue)
            threadVm.CustomerId = item.CustomerId;

        CurrentThreadViewModel = threadVm;
    }

    private void ClearThreadPanel()
    {
        SelectedThread = null;
        CurrentThreadViewModel?.Detach();
        CurrentThreadViewModel = null;
        CustomerPanel = null;
    }

    private void DetachIfActiveThread(int threadId)
    {
        if (CurrentThreadViewModel is { } vm && vm.ThreadId == threadId)
        {
            vm.Detach();
            CurrentThreadViewModel = null;
            CustomerPanel = null;
        }
    }

    #endregion

    #region Selection Helpers

    private void ExitSelectionMode()
    {
        foreach (var t in Threads)
            t.IsChecked = false;
        IsSelectionMode = false;
        CheckedCount = 0;
    }

    private void RefreshCheckedCount()
    {
        CheckedCount = Threads.Count(t => t.IsChecked);
    }

    private static bool ConfirmAction(string message, string title)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
    }

    private async Task ExecuteDeleteAsync(Func<Task> deleteAction)
    {
        IsBusy = true;
        ClearError();
        try
        {
            await deleteAction();
            ExitSelectionMode();
            await LoadInboxAsync();
        }
        catch (Exception ex)
        {
            SetError($"Failed to delete: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region SignalR

    private void OnSignalRMessageReceived(JsonElement message, JsonElement thread)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            int threadId = message.TryGetProperty("threadId", out var tidEl)
                ? tidEl.GetInt32() : 0;

            if (threadId == 0)
            {
                await LoadInboxCommand.ExecuteAsync(null);
                return;
            }

            var existing = Threads.FirstOrDefault(t => t.ThreadId == threadId);
            if (existing is null)
            {
                await LoadInboxCommand.ExecuteAsync(null);
                return;
            }

            if (message.TryGetProperty("body", out var bodyEl))
                existing.LastMessageBody = bodyEl.GetString() ?? "";
            if (message.TryGetProperty("direction", out var dirEl))
                existing.LastMessageDirection = dirEl.GetString() ?? "";
            if (thread.TryGetProperty("unreadCount", out var unreadEl))
                existing.UnreadCount = unreadEl.GetInt32();

            existing.LastMessageTime = FormatInboxTime(DateTime.UtcNow);

            int idx = Threads.IndexOf(existing);
            if (idx > 0)
                Threads.Move(idx, 0);

            TotalThreads = Threads.Count;
        });
    }

    #endregion

    #region Collection Merge

    /// <summary>
    /// Updates <see cref="Threads"/> in-place: removes stale, updates existing, inserts new.
    /// Avoids replacing the collection to prevent UI flicker.
    /// </summary>
    private void MergeInbox(List<InboxThreadItem> freshItems)
    {
        var currentMap = Threads.ToDictionary(t => t.ThreadId);
        var freshIds = new HashSet<int>(freshItems.Select(t => t.ThreadId));

        for (int i = Threads.Count - 1; i >= 0; i--)
        {
            if (!freshIds.Contains(Threads[i].ThreadId))
                Threads.RemoveAt(i);
        }

        for (int targetIdx = 0; targetIdx < freshItems.Count; targetIdx++)
        {
            var fresh = freshItems[targetIdx];

            if (currentMap.TryGetValue(fresh.ThreadId, out var existing))
            {
                CopyThreadProperties(fresh, existing);

                int currentIdx = Threads.IndexOf(existing);
                if (currentIdx >= 0 && currentIdx != targetIdx)
                    Threads.Move(currentIdx, targetIdx);
            }
            else
            {
                Threads.Insert(Math.Min(targetIdx, Threads.Count), fresh);
            }
        }
    }

    private static void CopyThreadProperties(InboxThreadItem source, InboxThreadItem target)
    {
        target.CustomerName = source.CustomerName;
        target.DisplayName = source.DisplayName;
        target.CustomerPhone = source.CustomerPhone;
        target.LastMessageBody = source.LastMessageBody;
        target.LastMessageDirection = source.LastMessageDirection;
        target.LastMessageTime = source.LastMessageTime;
        target.UnreadCount = source.UnreadCount;
        target.Status = source.Status;
        target.CustomerId = source.CustomerId;
        target.AvatarLetter = source.AvatarLetter;
    }

    #endregion

    #region JSON Parsing

    private static InboxThreadItem ParseThreadItem(JsonElement json)
    {
        var item = new InboxThreadItem
        {
            ThreadId = json.GetProperty("thread_id").GetInt32(),
            CustomerPhone = json.TryGetProperty("contact_phone", out var contactPhone)
                            && contactPhone.ValueKind == JsonValueKind.String
                ? contactPhone.GetString() ?? string.Empty
                : string.Empty,
            UnreadCount = json.GetProperty("unread_count").GetInt32(),
            Status = json.GetProperty("status").GetString() ?? "Open"
        };

        ParseTimestamp(json, item);
        ParseCustomer(json, item);
        ParseLastMessage(json, item);

        return item;
    }

    private static void ParseTimestamp(JsonElement json, InboxThreadItem item)
    {
        if (json.TryGetProperty("last_message_at", out var el) &&
            el.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(el.GetString(), out DateTime dt))
        {
            item.LastMessageTime = FormatInboxTime(dt);
        }
    }

    private static void ParseCustomer(JsonElement json, InboxThreadItem item)
    {
        if (!json.TryGetProperty("customer", out var cust) ||
            cust.ValueKind != JsonValueKind.Object)
        {
            item.DisplayName = string.IsNullOrWhiteSpace(item.CustomerPhone)
                ? "Unknown" : item.CustomerPhone;
            item.AvatarLetter = GetAvatarLetter(item.DisplayName, item.CustomerPhone);
            return;
        }

        string name = cust.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString() ?? "" : "";
        item.CustomerName = string.IsNullOrWhiteSpace(name) ? item.CustomerPhone : name;

        if (cust.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            item.CustomerId = idEl.GetInt32();

        string? first = cust.TryGetProperty("first_name", out var fn) ? fn.GetString() : null;
        string? last = cust.TryGetProperty("last_name", out var ln) ? ln.GetString() : null;
        item.DisplayName = FormatDisplayName(first, last, item.CustomerName, item.CustomerPhone);
        item.AvatarLetter = GetAvatarLetter(item.DisplayName, item.CustomerPhone);
    }

    private static void ParseLastMessage(JsonElement json, InboxThreadItem item)
    {
        if (!json.TryGetProperty("last_message", out var lm) ||
            lm.ValueKind != JsonValueKind.Object)
            return;

        item.LastMessageBody = lm.TryGetProperty("body", out var bodyEl)
            ? bodyEl.GetString() ?? "" : "";
        item.LastMessageDirection = lm.TryGetProperty("direction", out var dirEl)
            ? dirEl.GetString() ?? "" : "";

        if (!string.IsNullOrWhiteSpace(item.CustomerPhone))
            return;

        string? toPhone = lm.TryGetProperty("to_phone", out var tpE) ? tpE.GetString() : null;
        string? fromPhone = lm.TryGetProperty("from_phone", out var fpE) ? fpE.GetString() : null;
        string? inferredPhone = item.LastMessageDirection == "Outbound" ? toPhone : fromPhone;

        if (string.IsNullOrWhiteSpace(inferredPhone))
            return;

        item.CustomerPhone = inferredPhone;
        if (item.DisplayName == "Unknown")
        {
            item.DisplayName = inferredPhone;
            item.AvatarLetter = GetAvatarLetter(item.DisplayName, item.CustomerPhone);
        }
    }

    #endregion

    #region Formatting Utilities

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

    private static string FormatDisplayName(string? first, string? last, string fallback, string phone)
    {
        if (!string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(first))
            return $"{last.Trim()}, {first.Trim()}";
        if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        if (!string.IsNullOrWhiteSpace(last)) return last.Trim();
        if (!string.IsNullOrWhiteSpace(fallback) && fallback != "Unknown")
            return fallback;
        return string.IsNullOrWhiteSpace(phone) ? "Unknown" : phone;
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

    #endregion
}
