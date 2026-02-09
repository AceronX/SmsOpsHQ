using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
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
}

// Compose ViewModel: send new messages or use quick reply templates.
public sealed partial class ComposeViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly Action? _onMessageSent;

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

    public ComposeViewModel(ApiClient apiClient, AppState appState, Action? onMessageSent = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _onMessageSent = onMessageSent;
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
                ThreadId = ThreadId
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
}
