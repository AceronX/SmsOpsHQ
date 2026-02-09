using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Twilio number display item.
public sealed class TwilioNumberItem
{
    public int NumberId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Sid { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

// Settings ViewModel with 7 tabs: Database, Phone Numbers, Twilio, Reminders, VoIP, Presets, General.
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Tab 1: Database
    [ObservableProperty]
    private string _databaseStatus = "Not tested";

    [ObservableProperty]
    private string _databaseDetails = string.Empty;

    // Tab 2: Phone Numbers
    [ObservableProperty]
    private ObservableCollection<TwilioNumberItem> _phoneNumbers = new();

    [ObservableProperty]
    private string _newPhoneNumber = string.Empty;

    // Tab 3: Twilio
    [ObservableProperty]
    private string _twilioSid = string.Empty;

    [ObservableProperty]
    private string _twilioToken = string.Empty;

    // Tab 4: Reminders
    [ObservableProperty]
    private bool _schedulerRunning;

    [ObservableProperty]
    private string _schedulerNextRun = string.Empty;

    [ObservableProperty]
    private int _dailySent;

    [ObservableProperty]
    private int _dailyLimit;

    // Tab 5: VoIP
    [ObservableProperty]
    private string _xblueIp = string.Empty;

    [ObservableProperty]
    private bool _xblueEnabled;

    // Tab 7: General
    [ObservableProperty]
    private string _syncStatus = "Unknown";

    [ObservableProperty]
    private string _appVersion = "SmsOps HQ v1.0.0";

    public SettingsViewModel(ApiClient apiClient, AppState appState)
    {
        _apiClient = apiClient;
        _appState = appState;
    }

    [RelayCommand]
    private async Task TestDatabaseAsync()
    {
        IsBusy = true;
        try
        {
            JsonElement result = await _apiClient.TestSqliteAsync();
            bool success = result.TryGetProperty("success", out JsonElement sE) && sE.GetBoolean();
            DatabaseStatus = success ? "Connected" : "Error";

            if (success)
            {
                int customers = result.TryGetProperty("customers", out JsonElement cE) ? cE.GetInt32() : 0;
                int tickets = result.TryGetProperty("tickets", out JsonElement tE) ? tE.GetInt32() : 0;
                int active = result.TryGetProperty("active_tickets", out JsonElement aE) ? aE.GetInt32() : 0;
                DatabaseDetails = $"Customers: {customers}, Tickets: {tickets}, Active: {active}";
            }
            else
            {
                DatabaseDetails = result.TryGetProperty("error", out JsonElement eE) ? eE.GetString() ?? "Unknown error" : "Unknown error";
            }
        }
        catch (Exception ex)
        {
            DatabaseStatus = "Error";
            DatabaseDetails = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadPhoneNumbersAsync()
    {
        try
        {
            JsonElement result = await _apiClient.GetTwilioNumbersAsync(_appState.CurrentStoreId);
            ObservableCollection<TwilioNumberItem> items = new();

            foreach (JsonElement n in result.EnumerateArray())
            {
                items.Add(new TwilioNumberItem
                {
                    NumberId = n.TryGetProperty("id", out JsonElement idE) ? idE.GetInt32() : 0,
                    Phone = n.TryGetProperty("phone", out JsonElement phE) ? phE.GetString() ?? "" : "",
                    Sid = n.TryGetProperty("sid", out JsonElement sidE) ? sidE.GetString() : null,
                    IsDefault = n.TryGetProperty("is_default", out JsonElement defE) && defE.GetBoolean(),
                    IsActive = n.TryGetProperty("is_active", out JsonElement acE) && acE.GetBoolean()
                });
            }

            PhoneNumbers = items;
        }
        catch (Exception ex)
        {
            SetError($"Load numbers failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddPhoneNumberAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPhoneNumber)) return;

        try
        {
            await _apiClient.AddNumberAsync(_appState.CurrentStoreId, NewPhoneNumber);
            NewPhoneNumber = string.Empty;
            await LoadPhoneNumbersAsync();
        }
        catch (Exception ex)
        {
            SetError($"Add number failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SetDefaultNumberAsync(int numberId)
    {
        try
        {
            await _apiClient.SetDefaultNumberAsync(_appState.CurrentStoreId, numberId);
            await LoadPhoneNumbersAsync();
        }
        catch (Exception ex)
        {
            SetError($"Set default failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeletePhoneNumberAsync(int numberId)
    {
        try
        {
            await _apiClient.DeleteNumberAsync(_appState.CurrentStoreId, numberId);
            await LoadPhoneNumbersAsync();
        }
        catch (Exception ex)
        {
            SetError($"Delete number failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveTwilioConfigAsync()
    {
        try
        {
            await _apiClient.UpdateTwilioConfigAsync(_appState.CurrentStoreId, TwilioSid, TwilioToken);
        }
        catch (Exception ex)
        {
            SetError($"Save Twilio config failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadSchedulerStatusAsync()
    {
        try
        {
            SchedulerStatus? status = await _apiClient.GetSchedulerStatusAsync();
            if (status is not null)
            {
                SchedulerRunning = status.Running;
                SchedulerNextRun = status.NextRunTime ?? "N/A";
                DailySent = status.DailySent;
                DailyLimit = status.DailyLimit;
            }
        }
        catch (Exception ex)
        {
            SetError($"Scheduler status failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StartSchedulerAsync()
    {
        try
        {
            await _apiClient.StartSchedulerAsync();
            await LoadSchedulerStatusAsync();
        }
        catch (Exception ex)
        {
            SetError($"Start scheduler failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopSchedulerAsync()
    {
        try
        {
            await _apiClient.StopSchedulerAsync();
            await LoadSchedulerStatusAsync();
        }
        catch (Exception ex)
        {
            SetError($"Stop scheduler failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TriggerSyncAsync()
    {
        try
        {
            SyncStatus = "Syncing...";
            await _apiClient.TriggerSyncAsync();
            SyncStatus = "Sync triggered (running in background)";
        }
        catch (Exception ex)
        {
            SyncStatus = $"Sync failed: {ex.Message}";
        }
    }
}
