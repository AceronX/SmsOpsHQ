using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.Views;

namespace SmsOpsHQ.Desktop.ViewModels;

/// <summary>
/// Represents one message bubble in the conversation view.
/// Observable so status updates reflect without re-rendering the entire list.
/// </summary>
public sealed partial class MessageBubbleItem : ObservableObject
{
    public int MessageId { get; init; }

    [ObservableProperty] private string _direction = "Inbound";
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private string _category = "general";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _createdAt = string.Empty;
    [ObservableProperty] private string? _mediaJson;

    public bool IsOutbound => Direction == "Outbound";
    public bool IsNote => Direction == "Note";
}

/// <summary>
/// Thread conversation ViewModel — loads messages, supports compose, notes, and templates.
/// </summary>
public sealed partial class ThreadViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly SignalRClient _signalRClient;
    private readonly XBlueService? _xblueService;
    private readonly ISendSmsDialogService? _sendSmsDialogService;
    private readonly CustomerQualityQueryService? _qualityQueryService;
    private readonly Action<CustomerPanelViewModel?>? _setRightPanel;
    private readonly Action? _onCloseRequested;
    private readonly Action? _onMessagesLoaded;

    #region Observable Properties

    [ObservableProperty] private int _threadId;
    [ObservableProperty] private string _customerName = "Unknown";
    [ObservableProperty] private string _customerPhone = string.Empty;
    [ObservableProperty] private int? _customerId;
    [ObservableProperty] private ObservableCollection<MessageBubbleItem> _messages = new();
    [ObservableProperty] private string _composeText = string.Empty;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private CustomerPanelViewModel? _customerPanel;
    [ObservableProperty] private ObservableCollection<TemplateItem> _quickTemplates = new();
    [ObservableProperty] private TemplateItem? _selectedQuickTemplate;

    #endregion

    /// <summary>Fires when messages are loaded so the view can scroll to bottom.</summary>
    public event Action? MessagesLoaded;

    public ThreadViewModel(
        ApiClient apiClient,
        AppState appState,
        NavigationService navigation,
        SignalRClient signalRClient,
        int threadId,
        string customerName,
        XBlueService? xblueService = null,
        ISendSmsDialogService? sendSmsDialogService = null,
        CustomerQualityQueryService? qualityQueryService = null,
        Action<CustomerPanelViewModel?>? setRightPanel = null,
        Action? onCloseRequested = null,
        Action? onMessagesLoaded = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _navigation = navigation;
        _signalRClient = signalRClient;
        _xblueService = xblueService;
        _sendSmsDialogService = sendSmsDialogService;
        _qualityQueryService = qualityQueryService;
        _setRightPanel = setRightPanel;
        _onCloseRequested = onCloseRequested;
        _onMessagesLoaded = onMessagesLoaded;
        ThreadId = threadId;
        CustomerName = customerName;

        _signalRClient.MessageReceived += OnSignalRMessageReceived;
    }

    partial void OnSelectedQuickTemplateChanged(TemplateItem? value)
    {
        if (value is not null)
            ComposeText = value.Body;
    }

    /// <summary>Unsubscribes SignalR handler; call when this VM is no longer the active thread.</summary>
    public void Detach()
    {
        _signalRClient.MessageReceived -= OnSignalRMessageReceived;
    }

    #region Commands

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        try
        {
            JsonElement result = await _apiClient.GetTemplatesAsync(_appState.CurrentStoreId);
            var items = new ObservableCollection<TemplateItem>();

            foreach (JsonElement t in result.EnumerateArray())
            {
                items.Add(new TemplateItem
                {
                    TemplateId = t.TryGetProperty("id", out var idE) ? idE.GetInt32() : 0,
                    Name = t.TryGetProperty("name", out var nameE) ? nameE.GetString() ?? "" : "",
                    Body = t.TryGetProperty("body", out var bodyE) ? bodyE.GetString() ?? "" : "",
                    Hotkey = t.TryGetProperty("hotkey", out var hkE) ? hkE.GetString() : null
                });
            }

            QuickTemplates = items;
        }
        catch
        {
            // Templates are optional; don't block the thread view.
        }
    }

    [RelayCommand]
    private void OpenMediaViewer(MessageBubbleItem item)
    {
        if (string.IsNullOrEmpty(item.MediaJson)) return;

        try
        {
            var mediaUrls = JsonSerializer.Deserialize<List<string>>(item.MediaJson);
            if (mediaUrls is null || mediaUrls.Count == 0) return;
            _ = LoadAndShowMediaAsync(mediaUrls[0]);
        }
        catch
        {
            // Invalid media JSON; ignore.
        }
    }

    [RelayCommand]
    private async Task LoadMessagesAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            JsonElement result = await _apiClient.GetThreadAsync(_appState.CurrentStoreId, ThreadId);

            ParseCustomerInfo(result);

            var freshMessages = ParseMessages(result, out string? inferredPhone);

            if (string.IsNullOrWhiteSpace(CustomerPhone) && !string.IsNullOrWhiteSpace(inferredPhone))
            {
                CustomerPhone = inferredPhone;
                if (CustomerName == "Unknown")
                    CustomerName = inferredPhone;
            }

            bool hadMessages = Messages.Count > 0;
            MergeMessages(freshMessages);

            if (!hadMessages || freshMessages.Count > Messages.Count)
                MessagesLoaded?.Invoke();
            _onMessagesLoaded?.Invoke();

            await EnsureCustomerPanelAsync();
        }
        catch (Exception ex)
        {
            SetError($"Failed to load messages: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ComposeText)) return;

        IsBusy = true;
        ClearError();

        try
        {
            var request = new SendMessageRequest
            {
                StoreId = _appState.CurrentStoreId,
                ToPhone = CustomerPhone,
                Body = ComposeText,
                ThreadId = ThreadId,
                TwilioNumberId = _appState.CurrentTwilioNumberId
            };

            await _apiClient.SendMessageAsync(request);
            ComposeText = string.Empty;
            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            SetError($"Send failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(NoteText)) return;

        IsBusy = true;
        ClearError();

        try
        {
            var request = new CreateNoteRequest
            {
                StoreId = _appState.CurrentStoreId,
                Content = NoteText
            };

            await _apiClient.CreateNoteAsync(ThreadId, request);
            NoteText = string.Empty;
            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            SetError($"Note failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        Detach();

        if (_onCloseRequested is not null)
            _onCloseRequested();
        else
            _navigation.NavigateTo<InboxViewModel>();
    }

    #endregion

    #region SignalR

    private void OnSignalRMessageReceived(JsonElement message, JsonElement thread)
    {
        if (!message.TryGetProperty("threadId", out var tidElem) ||
            tidElem.GetInt32() != ThreadId)
            return;

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            int msgId = message.TryGetProperty("messageId", out var midEl) ? midEl.GetInt32() : 0;
            if (msgId <= 0) return;

            var existing = FindMessageById(msgId);
            if (existing is not null)
            {
                if (message.TryGetProperty("status", out var statusEl))
                    existing.Status = statusEl.GetString() ?? existing.Status;
                return;
            }

            var item = ParseSignalRMessage(message);
            Messages.Add(item);
            MessagesLoaded?.Invoke();
            _onMessagesLoaded?.Invoke();
        });
    }

    private MessageBubbleItem? FindMessageById(int messageId)
    {
        foreach (var m in Messages)
            if (m.MessageId == messageId)
                return m;
        return null;
    }

    private static MessageBubbleItem ParseSignalRMessage(JsonElement msg)
    {
        var item = new MessageBubbleItem
        {
            MessageId = msg.TryGetProperty("messageId", out var midE) ? midE.GetInt32() : 0,
            Direction = msg.TryGetProperty("direction", out var dE) ? dE.GetString() ?? "Inbound" : "Inbound",
            Body = msg.TryGetProperty("body", out var bE) ? bE.GetString() ?? "" : "",
            Status = msg.TryGetProperty("status", out var stE) ? stE.GetString() ?? "" : "",
            Category = msg.TryGetProperty("category", out var cE) ? cE.GetString() ?? "general" : "general",
            MediaJson = msg.TryGetProperty("mediaJson", out var mjE) && mjE.ValueKind == JsonValueKind.String
                ? mjE.GetString() : null
        };

        item.CreatedAt = msg.TryGetProperty("createdAt", out var caE) &&
                         DateTime.TryParse(caE.GetString(), out DateTime dt)
            ? dt.ToLocalTime().ToString("MMM d, h:mm tt")
            : DateTime.Now.ToString("MMM d, h:mm tt");

        return item;
    }

    #endregion

    #region Message Parsing & Merge

    private void ParseCustomerInfo(JsonElement result)
    {
        if (!result.TryGetProperty("thread", out var threadElem) ||
            !threadElem.TryGetProperty("customer", out var cust) ||
            cust.ValueKind != JsonValueKind.Object)
            return;

        string rawName = cust.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString() ?? "" : "";
        CustomerPhone = cust.TryGetProperty("phone", out var phoneEl)
            ? phoneEl.GetString() ?? "" : "";
        CustomerName = string.IsNullOrWhiteSpace(rawName)
            ? (string.IsNullOrWhiteSpace(CustomerPhone) ? "Unknown" : CustomerPhone)
            : rawName;

        if (cust.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            CustomerId = idEl.GetInt32();
    }

    /// <summary>
    /// Parses message array from API response. Returns them in chronological order (oldest first).
    /// </summary>
    private static List<MessageBubbleItem> ParseMessages(
        JsonElement result, out string? inferredPhone)
    {
        inferredPhone = null;
        var messages = new List<MessageBubbleItem>();

        if (!result.TryGetProperty("messages", out var msgsElem))
            return messages;

        var rawList = new List<JsonElement>();
        foreach (JsonElement m in msgsElem.EnumerateArray())
            rawList.Add(m);
        rawList.Reverse();

        foreach (JsonElement m in rawList)
        {
            var item = new MessageBubbleItem
            {
                MessageId = m.TryGetProperty("id", out var idE) ? idE.GetInt32() : 0,
                Direction = m.TryGetProperty("direction", out var dirE) ? dirE.GetString() ?? "Inbound" : "Inbound",
                Body = m.TryGetProperty("body", out var bodyE) ? bodyE.GetString() ?? "" : "",
                Category = m.TryGetProperty("category", out var catE) ? catE.GetString() ?? "general" : "general",
                Status = m.TryGetProperty("status", out var stE) ? stE.GetString() ?? "" : "",
                MediaJson = m.TryGetProperty("media_json", out var mjE) && mjE.ValueKind == JsonValueKind.String
                    ? mjE.GetString() : null
            };

            if (m.TryGetProperty("created_at", out var caE) &&
                caE.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(caE.GetString(), out DateTime dt))
            {
                item.CreatedAt = dt.ToLocalTime().ToString("MMM d, h:mm tt");
            }

            if (inferredPhone is null)
            {
                string? toP = m.TryGetProperty("to_phone", out var tpE) ? tpE.GetString() : null;
                string? fromP = m.TryGetProperty("from_phone", out var fpE) ? fpE.GetString() : null;
                inferredPhone = item.Direction == "Outbound" ? toP : fromP;
            }

            messages.Add(item);
        }

        return messages;
    }

    /// <summary>
    /// Updates <see cref="Messages"/> in-place: updates statuses, appends new.
    /// Avoids replacing the collection to prevent UI flicker.
    /// </summary>
    private void MergeMessages(List<MessageBubbleItem> freshItems)
    {
        var existingMap = new Dictionary<int, MessageBubbleItem>();
        foreach (var m in Messages)
            if (m.MessageId > 0)
                existingMap[m.MessageId] = m;

        var freshIds = new HashSet<int>(
            freshItems.Where(m => m.MessageId > 0).Select(m => m.MessageId));

        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].MessageId > 0 && !freshIds.Contains(Messages[i].MessageId))
                Messages.RemoveAt(i);
        }

        foreach (var fresh in freshItems)
        {
            if (existingMap.TryGetValue(fresh.MessageId, out var existing))
            {
                if (existing.Status != fresh.Status)
                    existing.Status = fresh.Status;
            }
            else
            {
                Messages.Add(fresh);
            }
        }
    }

    #endregion

    #region Helpers

    private async Task EnsureCustomerPanelAsync()
    {
        if (string.IsNullOrEmpty(CustomerPhone))
            return;
        if (CustomerPanel is not null && CustomerPanel.CustomerPhone == CustomerPhone)
            return;

        var panel = new CustomerPanelViewModel(_apiClient, _xblueService, _sendSmsDialogService, _qualityQueryService);
        CustomerPanel = panel;
        _setRightPanel?.Invoke(panel);
        await panel.LoadByPhoneAsync(CustomerPhone);
    }

    private async Task LoadAndShowMediaAsync(string mediaUrl)
    {
        try
        {
            byte[] imageData = await _apiClient.ProxyMediaAsync(mediaUrl);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var viewer = new MediaViewerView();
                viewer.LoadFromBytes(imageData, mediaUrl);
                viewer.Show();
            });
        }
        catch
        {
            // Media load failed; ignore.
        }
    }

    #endregion
}
