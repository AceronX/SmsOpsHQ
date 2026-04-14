using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Templates CRUD ViewModel.
public sealed partial class TemplatesViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;

    [ObservableProperty]
    private ObservableCollection<TemplateItem> _templates = new();

    [ObservableProperty]
    private TemplateItem? _selectedTemplate;

    // Edit fields
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editBody = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int? _editingTemplateId;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _editCategory = "General";

    public bool IsFilterAll => SelectedCategory == "All";
    public bool IsFilterGeneral => SelectedCategory == "General";
    public bool IsFilterReview => SelectedCategory == "Review";

    public TemplatesViewModel(ApiClient apiClient, AppState appState)
    {
        _apiClient = apiClient;
        _appState = appState;
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterGeneral));
        OnPropertyChanged(nameof(IsFilterReview));
        _ = LoadAsync();
    }

    partial void OnSelectedTemplateChanged(TemplateItem? value)
    {
        if (value is not null)
        {
            EditName = value.Name;
            EditBody = value.Body;
            EditCategory = value.Category;
            EditingTemplateId = value.TemplateId;
            IsEditing = true;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            string? categoryFilter = SelectedCategory == "All" ? null : SelectedCategory;
            JsonElement result = await _apiClient.GetTemplatesAsync(_appState.CurrentStoreId, categoryFilter);
            ObservableCollection<TemplateItem> items = new();

            foreach (JsonElement t in result.EnumerateArray())
            {
                items.Add(new TemplateItem
                {
                    TemplateId = t.TryGetProperty("id", out JsonElement idE) ? idE.GetInt32() : 0,
                    Name = t.TryGetProperty("name", out JsonElement nameE) ? nameE.GetString() ?? "" : "",
                    Body = t.TryGetProperty("body", out JsonElement bodyE) ? bodyE.GetString() ?? "" : "",
                    Hotkey = t.TryGetProperty("hotkey", out JsonElement hkE) ? hkE.GetString() : null,
                    IsGlobal = t.TryGetProperty("is_global", out JsonElement igE) && igE.GetBoolean(),
                    Category = t.TryGetProperty("category", out JsonElement catE) ? catE.GetString() ?? "General" : "General"
                });
            }

            Templates = items;
        }
        catch (Exception ex)
        {
            SetError($"Load failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewTemplate()
    {
        EditName = string.Empty;
        EditBody = string.Empty;
        EditCategory = SelectedCategory == "All" ? "General" : SelectedCategory;
        EditingTemplateId = null;
        IsEditing = true;
        SelectedTemplate = null;
    }

    [RelayCommand]
    private async Task SetCategoryAsync(string category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditBody))
        {
            SetError("Name and body are required.");
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            TemplateCreateRequest request = new()
            {
                Name = EditName,
                Body = EditBody,
                StoreId = _appState.CurrentStoreId,
                Category = EditCategory
            };

            if (EditingTemplateId.HasValue)
                await _apiClient.UpdateTemplateAsync(EditingTemplateId.Value, request);
            else
                await _apiClient.CreateTemplateAsync(request);

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetError($"Save failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (EditingTemplateId is null) return;

        IsBusy = true;
        ClearError();

        try
        {
            await _apiClient.DeleteTemplateAsync(EditingTemplateId.Value);
            IsEditing = false;
            EditingTemplateId = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetError($"Delete failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditingTemplateId = null;
        EditName = string.Empty;
        EditBody = string.Empty;
    }
}
