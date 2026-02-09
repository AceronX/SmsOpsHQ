using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.Views;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents one message bubble in the conversation view.
public sealed class MessageBubbleItem
{
    public int MessageId { get; set; }
    public string Direction { get; set; } = "Inbound";
    public string Body { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public bool IsOutbound => Direction == "Outbound";
    public bool IsNote => Direction == "Note";
    public string? MediaJson { get; set; }
}

// Thread conversation ViewModel: loads messages, supports compose and notes.
public sealed partial class ThreadViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly SignalRClient _signalRClient;
    private readonly XBlueService? _xblueService;

    [ObservableProperty]
    private int _threadId;

    [ObservableProperty]
    private string _customerName = "Unknown";

    [ObservableProperty]
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private int? _customerId;

    [ObservableProperty]
    private ObservableCollection<MessageBubbleItem> _messages = new();

    [ObservableProperty]
    private string _composeText = string.Empty;

    [ObservableProperty]
    private string _noteText = string.Empty;

    [ObservableProperty]
    private CustomerPanelViewModel? _customerPanel;

    [ObservableProperty]
    private ObservableCollection<TemplateItem> _quickTemplates = new();

    [ObservableProperty]
    private TemplateItem? _selectedQuickTemplate;

    // Fires when messages are loaded so the view can scroll to bottom.
    public event Action? MessagesLoaded;

    public ThreadViewModel(
        ApiClient apiClient, AppState appState,
        NavigationService navigation, SignalRClient signalRClient,
        int threadId, string customerName,
        XBlueService? xblueService = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _navigation = navigation;
        _signalRClient = signalRClient;
        _xblueService = xblueService;
        ThreadId = threadId;
        CustomerName = customerName;

        _signalRClient.MessageReceived += OnSignalRMessageReceived;
    }

    partial void OnSelectedQuickTemplateChanged(TemplateItem? value)
    {
        if (value is not null)
            ComposeText = value.Body;
    }

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        try
        {
            JsonElement result = await _apiClient.GetTemplatesAsync(_appState.CurrentStoreId);
            ObservableCollection<TemplateItem> items = new();

            foreach (JsonElement t in result.EnumerateArray())
            {
                items.Add(new TemplateItem
                {
                    TemplateId = t.TryGetProperty("id", out JsonElement idE) ? idE.GetInt32() : 0,
                    Name = t.TryGetProperty("name", out JsonElement nameE) ? nameE.GetString() ?? "" : "",
                    Body = t.TryGetProperty("body", out JsonElement bodyE) ? bodyE.GetString() ?? "" : "",
                    Hotkey = t.TryGetProperty("hotkey", out JsonElement hkE) ? hkE.GetString() : null
                });
            }

            QuickTemplates = items;
        }
        catch
        {
            // Templates are optional; don't block thread view.
        }
    }

    [RelayCommand]
    private void OpenMediaViewer(MessageBubbleItem item)
    {
        if (string.IsNullOrEmpty(item.MediaJson)) return;

        try
        {
            List<string>? mediaUrls = JsonSerializer.Deserialize<List<string>>(item.MediaJson);
            if (mediaUrls is null || mediaUrls.Count == 0) return;

            _ = LoadAndShowMediaAsync(mediaUrls[0]);
        }
        catch
        {
            // Invalid media JSON; ignore.
        }
    }

    private async Task LoadAndShowMediaAsync(string mediaUrl)
    {
        try
        {
            byte[] imageData = await _apiClient.ProxyMediaAsync(mediaUrl);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                MediaViewerView viewer = new();
                viewer.LoadFromBytes(imageData, mediaUrl);
                viewer.Show();
            });
        }
        catch
        {
            // Media load failed; ignore.
        }
    }

    private void OnSignalRMessageReceived(JsonElement message, JsonElement thread)
    {
        if (message.TryGetProperty("threadId", out JsonElement tidElem) && tidElem.GetInt32() == ThreadId)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => LoadMessagesCommand.ExecuteAsync(null));
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

            // Parse customer info
            if (result.TryGetProperty("thread", out JsonElement threadElem) &&
                threadElem.TryGetProperty("customer", out JsonElement custElem) &&
                custElem.ValueKind == JsonValueKind.Object)
            {
                CustomerName = custElem.TryGetProperty("name", out JsonElement nameElem)
                    ? nameElem.GetString() ?? "Unknown" : "Unknown";
                CustomerPhone = custElem.TryGetProperty("phone", out JsonElement phoneElem)
                    ? phoneElem.GetString() ?? "" : "";
                if (custElem.TryGetProperty("id", out JsonElement idElem) && idElem.ValueKind == JsonValueKind.Number)
                    CustomerId = idElem.GetInt32();
            }

            // Parse messages
            ObservableCollection<MessageBubbleItem> newMessages = new();
            if (result.TryGetProperty("messages", out JsonElement msgsElem))
            {
                // Messages come in DESC order from API; reverse for display.
                List<JsonElement> messagesList = new();
                foreach (JsonElement m in msgsElem.EnumerateArray())
                    messagesList.Add(m);
                messagesList.Reverse();

                foreach (JsonElement m in messagesList)
                {
                    MessageBubbleItem item = new()
                    {
                        MessageId = m.TryGetProperty("id", out JsonElement idE) ? idE.GetInt32() : 0,
                        Direction = m.TryGetProperty("direction", out JsonElement dirE) ? dirE.GetString() ?? "Inbound" : "Inbound",
                        Body = m.TryGetProperty("body", out JsonElement bodyE) ? bodyE.GetString() ?? "" : "",
                        Category = m.TryGetProperty("category", out JsonElement catE) ? catE.GetString() ?? "general" : "general",
                        Status = m.TryGetProperty("status", out JsonElement stE) ? stE.GetString() ?? "" : "",
                        MediaJson = m.TryGetProperty("media_json", out JsonElement mjE) && mjE.ValueKind == JsonValueKind.String ? mjE.GetString() : null
                    };

                    if (m.TryGetProperty("created_at", out JsonElement caE) && caE.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(caE.GetString(), out DateTime dt))
                            item.CreatedAt = dt.ToLocalTime().ToString("MMM d, h:mm tt");
                    }

                    newMessages.Add(item);
                }
            }

            Messages = newMessages;
            MessagesLoaded?.Invoke();

            // Load customer panel if we have a customer ID.
            if (CustomerId.HasValue && (CustomerPanel is null || CustomerPanel.CustomerId != CustomerId.Value))
            {
                CustomerPanelViewModel panel = new(_apiClient, _xblueService)
                {
                    CustomerId = CustomerId.Value,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone
                };
                CustomerPanel = panel;
                await panel.LoadContextCommand.ExecuteAsync(null);
            }

            // Mark thread as read.
            // The API handles this server-side via the GET /api/thread/{id} call already.
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
            SendMessageRequest request = new()
            {
                StoreId = _appState.CurrentStoreId,
                ToPhone = CustomerPhone,
                Body = ComposeText,
                ThreadId = ThreadId
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
            CreateNoteRequest request = new()
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
        _signalRClient.MessageReceived -= OnSignalRMessageReceived;
        _navigation.NavigateTo<InboxViewModel>();
    }
}
