using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents a template for quick reply selection.
public sealed class TemplateItem
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Hotkey { get; set; }
    public bool IsGlobal { get; set; }
    public string Category { get; set; } = "General";
}

// Compose ViewModel: send new messages or use quick reply templates.
public sealed partial class ComposeViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action? _onMessageSent;
    private readonly Action? _onCancelRequested;
    private readonly Action<string?>? _onPhoneForPreview;

    private CancellationTokenSource? _phonePreviewCts;
    private const int PhonePreviewDebounceMs = 450;

    [ObservableProperty]
    private string _toPhone = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private int? _threadId;

    [ObservableProperty]
    private ObservableCollection<TemplateItem> _templates = new();

    [ObservableProperty]
    private TemplateItem? _selectedTemplate;

    [ObservableProperty]
    private bool _isSent;

    /// <summary>Character count of Body (for UX display).</summary>
    [ObservableProperty]
    private int _bodyCharacterCount;

    /// <summary>e.g. "1 SMS" or "2 SMS" for segment hint.</summary>
    [ObservableProperty]
    private string _smsSegmentLabel = "0 chars";

    public ComposeViewModel(ApiClient apiClient, AppState appState, Action? onMessageSent = null, Action? onCancelRequested = null, Action<string?>? onPhoneForPreview = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _onMessageSent = onMessageSent;
        _onCancelRequested = onCancelRequested;
        _onPhoneForPreview = onPhoneForPreview;
    }

    partial void OnToPhoneChanged(string value)
    {
        if (_onPhoneForPreview is null) return;

        _phonePreviewCts?.Cancel();

        bool validPhone = !string.IsNullOrWhiteSpace(value) && PhoneUtils.ExtractLast10Digits(value) is not null;
        if (!validPhone)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => _onPhoneForPreview(null));
            return;
        }

        _phonePreviewCts = new CancellationTokenSource();
        CancellationToken ct = _phonePreviewCts.Token;
        string capturedPhone = value.Trim();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PhonePreviewDebounceMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() => _onPhoneForPreview(capturedPhone));
        }, ct);
    }

    partial void OnBodyChanged(string value)
    {
        string s = value ?? "";
        BodyCharacterCount = s.Length;
        SmsSegmentLabel = GetSmsSegmentLabel(s.Length);
    }

    private static string GetSmsSegmentLabel(int length)
    {
        if (length == 0) return "0 chars";
        if (length <= 160) return $"{length} / 160 · 1 SMS";
        int segments = 1 + (int)Math.Ceiling((length - 160) / 153.0);
        return $"{length} chars · {segments} SMS";
    }

    partial void OnSelectedTemplateChanged(TemplateItem? value)
    {
        if (value is not null)
            Body = value.Body;
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
                    Hotkey = t.TryGetProperty("hotkey", out JsonElement hkE) ? hkE.GetString() : null,
                    IsGlobal = t.TryGetProperty("is_global", out JsonElement igE) && igE.GetBoolean()
                });
            }

            Templates = items;
        }
        catch
        {
            // Templates are optional; don't block compose.
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(ToPhone) || string.IsNullOrWhiteSpace(Body))
        {
            SetError("Phone and message body are required.");
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            SendMessageRequest request = new()
            {
                StoreId = _appState.CurrentStoreId,
                ToPhone = ToPhone,
                Body = Body,
                ThreadId = ThreadId,
                TwilioNumberId = _appState.CurrentTwilioNumberId
            };

            await _apiClient.SendMessageAsync(request);
            IsSent = true;
            Body = string.Empty;
            _onMessageSent?.Invoke();
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
    private void Cancel()
    {
        _onCancelRequested?.Invoke();
    }
}
