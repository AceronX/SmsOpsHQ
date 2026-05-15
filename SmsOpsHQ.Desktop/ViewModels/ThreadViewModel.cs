using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    [ObservableProperty] private ImageSource? _mediaThumbnail;
    [ObservableProperty] private bool _isThumbnailLoading;
    [ObservableProperty] private string _mediaContentType = string.Empty;
    [ObservableProperty] private string _mediaLabel = string.Empty;
    [ObservableProperty] private string _mediaIconGlyph = string.Empty;
    [ObservableProperty] private bool _isNonImageMedia;
    [ObservableProperty] private bool _isVideoMedia;
    public string? TempFilePath { get; set; }

    public bool IsOutbound => Direction == "Outbound";
    public bool IsNote => Direction == "Note";
    public bool HasMedia => !string.IsNullOrEmpty(MediaJson);
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
            var (url, contentType) = ExtractFirstMedia(item.MediaJson);
            if (url is null) return;

            bool isImage = string.IsNullOrEmpty(contentType) || contentType.StartsWith("image/");
            if (isImage)
                _ = LoadAndShowMediaAsync(url);
            else
                _ = DownloadAndOpenWithSystemAsync(url, contentType!, item.TempFilePath);
        }
        catch
        {
            // Invalid media JSON; ignore.
        }
    }

    private static (string? Url, string? ContentType) ExtractFirstMedia(string mediaJson)
    {
        using var doc = JsonDocument.Parse(mediaJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, null);

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                return (el.GetString(), null);
            if (el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("url", out var urlEl))
            {
                string? ct = el.TryGetProperty("content_type", out var ctEl) ? ctEl.GetString() : null;
                return (urlEl.GetString(), ct);
            }
        }
        return (null, null);
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
            LoadThumbnailsForMessages(Messages);

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

            JsonElement response = await _apiClient.SendMessageAsync(request);

            // Surface mock-mode loudly: the message was recorded but never sent.
            bool mock = response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("mock", out JsonElement mockEl)
                && mockEl.ValueKind == JsonValueKind.True;

            ComposeText = string.Empty;
            await LoadMessagesAsync();

            if (mock)
            {
                SetError(
                    "MOCK MODE: Twilio is not configured, so the customer did NOT receive this message. " +
                    "Open Settings → Twilio to enter your Account SID and Auth Token.");
            }
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
            LoadThumbnailsForMessages(new[] { item });
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

    private async Task DownloadAndOpenWithSystemAsync(string mediaUrl, string contentType, string? cachedPath = null)
    {
        try
        {
            string tempPath;
            if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
            {
                tempPath = cachedPath;
            }
            else
            {
                byte[] data = await _apiClient.ProxyMediaAsync(mediaUrl);
                string ext = GetFileExtension(contentType);
                tempPath = Path.Combine(Path.GetTempPath(), $"SmsOpsHQ_{Guid.NewGuid():N}{ext}");
                await File.WriteAllBytesAsync(tempPath, data);
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            });
        }
        catch
        {
            // Download/open failed; ignore.
        }
    }

    private static string GetFileExtension(string contentType) => contentType switch
    {
        "video/mp4" => ".mp4",
        "video/quicktime" => ".mov",
        "video/webm" => ".webm",
        "video/3gpp" => ".3gp",
        "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        "audio/wav" => ".wav",
        "application/pdf" => ".pdf",
        _ when contentType.Contains("word") => ".docx",
        _ when contentType.StartsWith("video/") => ".mp4",
        _ when contentType.StartsWith("audio/") => ".mp3",
        _ => ".bin"
    };

    private void LoadThumbnailsForMessages(IEnumerable<MessageBubbleItem> items)
    {
        foreach (var item in items)
        {
            if (!item.HasMedia || !string.IsNullOrEmpty(item.MediaLabel)) continue;

            var (url, contentType) = ExtractFirstMedia(item.MediaJson!);
            if (url is null) continue;

            var (label, iconGlyph, isNonImage) = ClassifyMedia(contentType);
            item.MediaContentType = contentType ?? string.Empty;
            item.MediaLabel = label;
            item.MediaIconGlyph = iconGlyph;
            item.IsNonImageMedia = isNonImage;

            item.IsVideoMedia = contentType?.StartsWith("video/") == true;

            if (!isNonImage)
                _ = LoadThumbnailAsync(item, url);
            else
                _ = LoadNonImageThumbnailAsync(item, url, contentType!);
        }
    }

    private static (string Label, string IconGlyph, bool IsNonImage) ClassifyMedia(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType) || contentType.StartsWith("image/"))
            return ("Image", "\uE91B", false);
        if (contentType.StartsWith("video/"))
            return ("Video", "\uE714", true);
        if (contentType.StartsWith("audio/"))
            return ("Audio", "\uE8D6", true);
        if (contentType == "application/pdf")
            return ("PDF Document", "\uE8A5", true);
        if (contentType.Contains("word") || contentType.Contains("document"))
            return ("Document", "\uE8A5", true);
        return ("File", "\uE8A5", true);
    }

    private async Task LoadThumbnailAsync(MessageBubbleItem item, string mediaUrl)
    {
        item.IsThumbnailLoading = true;
        try
        {
            byte[] imageData = await _apiClient.ProxyMediaAsync(mediaUrl);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(imageData);
                bmp.DecodePixelWidth = 240;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                item.MediaThumbnail = bmp;
            });
        }
        catch
        {
            // Thumbnail load failed; "View attachment" link remains as fallback.
        }
        finally
        {
            item.IsThumbnailLoading = false;
        }
    }

    private async Task LoadNonImageThumbnailAsync(MessageBubbleItem item, string mediaUrl, string contentType)
    {
        item.IsThumbnailLoading = true;
        try
        {
            byte[] data = await _apiClient.ProxyMediaAsync(mediaUrl);
            string ext = GetFileExtension(contentType);
            string tempPath = Path.Combine(Path.GetTempPath(), $"SmsOpsHQ_{Guid.NewGuid():N}{ext}");
            await File.WriteAllBytesAsync(tempPath, data);
            item.TempFilePath = tempPath;

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                BitmapSource? thumb = GetShellThumbnail(tempPath, 240);
                if (thumb is not null)
                    item.MediaThumbnail = thumb;
            });
        }
        catch
        {
            // Shell thumbnail failed; icon placeholder remains.
        }
        finally
        {
            item.IsThumbnailLoading = false;
        }
    }

    #region Shell Thumbnail (Windows Explorer-style preview)

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int w, int h)
    {
        public int Width = w;
        public int Height = h;
    }

    private static BitmapSource? GetShellThumbnail(string filePath, int size)
    {
        try
        {
            Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out var factory);

            var nativeSize = new NativeSize(size, size);
            int hr = factory.GetImage(nativeSize, 0x08, out IntPtr hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                hr = factory.GetImage(nativeSize, 0x00, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero) return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #endregion
}
