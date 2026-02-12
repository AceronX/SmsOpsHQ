using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
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

// Store option for dropdown (Settings > Phone Numbers).
public sealed class StoreItem
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public int DefaultNumberId { get; set; } = 0; // 0 means no Twilio number set
}

// Settings ViewModel with 6 tabs: Credentials, Database, Phone Numbers, Twilio, Reminders, VoIP.
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly TwilioConfigService _twilioConfig;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Tab 0: Credentials
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _oldPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmNewPassword = string.Empty;

    [ObservableProperty]
    private bool _showOldPassword;

    [ObservableProperty]
    private bool _showNewPassword;

    [ObservableProperty]
    private bool _showConfirmPassword;

    [ObservableProperty]
    private string _credentialErrorMessage = string.Empty;

    [ObservableProperty]
    private string _credentialSuccessMessage = string.Empty;

    // Tab 1: Database
    [ObservableProperty]
    private string _databaseStatus = "Not tested";

    [ObservableProperty]
    private string _databaseDetails = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _xpdFilePath = string.Empty;

    [ObservableProperty]
    private string _xpdUser = string.Empty;

    [ObservableProperty]
    private string _xpdPassword = string.Empty;

    [ObservableProperty]
    private bool _showXpdPassword;

    [ObservableProperty]
    private string _xpdMdwPath = string.Empty;

    [ObservableProperty]
    private string _databaseErrorMessage = string.Empty;

    [ObservableProperty]
    private string _databaseSuccessMessage = string.Empty;

    [ObservableProperty]
    private string _lastSyncInfo = string.Empty;

    [ObservableProperty]
    private bool _syncInProgress;

    [ObservableProperty]
    private string _syncProgressMessage = string.Empty;

    [ObservableProperty]
    private int _syncProgressPercent;

    // Tab 2: Phone Numbers — store selection + Twilio numbers
    [ObservableProperty]
    private ObservableCollection<StoreItem> _availableStores = new();

    [ObservableProperty]
    private int _selectedStoreId;

    [ObservableProperty]
    private string _storeSaveMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TwilioNumberItem> _phoneNumbers = new();

    [ObservableProperty]
    private string _newPhoneNumber = string.Empty;

    [ObservableProperty]
    private string _newStoreName = string.Empty;

    /// <summary>True when the current user is HQ (can create stores).</summary>
    public bool IsHqUser => _appState.IsHqUser;

    /// <summary>Current store name for read-only display (no combobox).</summary>
    public string CurrentStoreDisplay => string.IsNullOrWhiteSpace(_appState.CurrentStoreName) ? "—" : _appState.CurrentStoreName;

    // Tab 3: Twilio
    [ObservableProperty]
    private string _twilioSid = string.Empty;

    [ObservableProperty]
    private string _twilioToken = string.Empty;

    [ObservableProperty]
    private bool _showTwilioToken;

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

    public SettingsViewModel(ApiClient apiClient, AppState appState, TwilioConfigService twilioConfig)
    {
        _apiClient = apiClient;
        _appState = appState;
        _twilioConfig = twilioConfig;
        SelectedStoreId = _appState.CurrentStoreId;
        LoadTwilioConfigFromFile();
    }

    private void LoadTwilioConfigFromFile()
    {
        var (sid, token) = _twilioConfig.Load();
        TwilioSid = sid ?? string.Empty;
        TwilioToken = token ?? string.Empty;
    }

    [RelayCommand]
    private async Task LoadCredentialsAsync()
    {
        Username = _appState.CurrentUser?.Username ?? string.Empty;
        OldPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        CredentialErrorMessage = string.Empty;
        CredentialSuccessMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveCredentialsAsync()
    {
        CredentialErrorMessage = string.Empty;
        CredentialSuccessMessage = string.Empty;

        bool changePassword = !string.IsNullOrWhiteSpace(OldPassword) ||
                              !string.IsNullOrWhiteSpace(NewPassword) ||
                              !string.IsNullOrWhiteSpace(ConfirmNewPassword);
        
        bool changeUsername = !string.IsNullOrWhiteSpace(Username) && 
                              Username.Trim() != (_appState.CurrentUser?.Username ?? string.Empty);

        if (!changePassword && !changeUsername)
        {
            CredentialSuccessMessage = "No changes to save.";
            return;
        }

        if (changePassword)
        {
            if (string.IsNullOrWhiteSpace(OldPassword))
            {
                CredentialErrorMessage = "Current password is required to set a new password.";
                return;
            }
            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                CredentialErrorMessage = "New password is required.";
                return;
            }
            if (NewPassword != ConfirmNewPassword)
            {
                CredentialErrorMessage = "New password and confirmation do not match.";
                return;
            }
            PasswordValidationResult validation = PasswordValidator.Validate(NewPassword);
            if (!validation.IsValid)
            {
                CredentialErrorMessage = validation.ErrorMessage ?? "New password does not meet requirements.";
                return;
            }
        }

        if (changeUsername && string.IsNullOrWhiteSpace(Username.Trim()))
        {
            CredentialErrorMessage = "Username cannot be empty.";
            return;
        }

        IsBusy = true;
        try
        {
            if (changeUsername)
            {
                await _apiClient.UpdateProfileAsync(Username.Trim(), null, null);
                // Update AppState
                if (_appState.CurrentUser is not null)
                {
                    _appState.UpdateCurrentUser(_appState.CurrentUser.StoreId, _appState.CurrentUser.TwilioNumberId);
                    _appState.CurrentUser = new UserDto
                    {
                        UserId = _appState.CurrentUser.UserId,
                        Username = Username.Trim(),
                        StoreId = _appState.CurrentUser.StoreId,
                        StoreName = _appState.CurrentUser.StoreName,
                        TwilioNumberId = _appState.CurrentUser.TwilioNumberId,
                        Role = _appState.CurrentUser.Role
                    };
                }
            }

            if (changePassword)
            {
                await _apiClient.ChangePasswordAsync(OldPassword, NewPassword);
                OldPassword = string.Empty;
                NewPassword = string.Empty;
                ConfirmNewPassword = string.Empty;
            }

            CredentialSuccessMessage = "Credentials saved successfully.";
        }
        catch (Exception ex)
        {
            CredentialErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadDatabaseConfigAsync()
    {
        DatabaseErrorMessage = string.Empty;
        DatabaseSuccessMessage = string.Empty;
        try
        {
            JsonElement config = await _apiClient.GetSyncConfigAsync();
            DatabasePath = config.TryGetProperty("sqlite_path", out JsonElement sp) ? sp.GetString() ?? "" : "";
            string xpd = config.TryGetProperty("xpd_path", out JsonElement xp) ? xp.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(xpd))
                XpdFilePath = xpd;
            string user = config.TryGetProperty("xpd_user", out JsonElement xu) ? xu.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(user))
                XpdUser = user;
            string mdw = config.TryGetProperty("mdw_path", out JsonElement mp) ? mp.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(mdw))
                XpdMdwPath = mdw;
            await RefreshSyncStatusAsync();
        }
        catch (Exception ex)
        {
            DatabaseErrorMessage = $"Could not load config: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestDatabaseAsync()
    {
        DatabaseErrorMessage = string.Empty;
        DatabaseSuccessMessage = string.Empty;
        IsBusy = true;
        try
        {
            string? pathToTest = string.IsNullOrWhiteSpace(DatabasePath) ? null : DatabasePath.Trim();
            JsonElement result = await _apiClient.TestSqliteAsync(pathToTest);
            bool success = result.TryGetProperty("success", out JsonElement sE) && sE.GetBoolean();
            DatabaseStatus = success ? "Connected" : "Error";

            if (success)
            {
                int customers = result.TryGetProperty("customers", out JsonElement cE) ? cE.GetInt32() : 0;
                int tickets = result.TryGetProperty("tickets", out JsonElement tE) ? tE.GetInt32() : 0;
                int items = result.TryGetProperty("items", out JsonElement iE) ? iE.GetInt32() : 0;
                int payments = result.TryGetProperty("payments", out JsonElement pE) ? pE.GetInt32() : 0;
                DatabaseDetails = $"Pawn data: {customers:N0} customers, {tickets:N0} tickets, {items:N0} items, {payments:N0} payments.";
                DatabaseSuccessMessage = "Database connection is OK.";
            }
            else
            {
                DatabaseDetails = result.TryGetProperty("error", out JsonElement eE) ? eE.GetString() ?? "Unknown error" : "Unknown error";
                DatabaseErrorMessage = "Connection test failed. Check that the database file exists and the API can access it.";
            }
        }
        catch (Exception ex)
        {
            DatabaseStatus = "Error";
            DatabaseDetails = ex.Message;
            DatabaseErrorMessage = "Could not reach the API or the database. Ensure the API is running and the connection string is correct.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TriggerXpdSyncAsync()
    {
        DatabaseErrorMessage = string.Empty;
        DatabaseSuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(XpdUser))
        {
            DatabaseErrorMessage = "XPD user is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(XpdPassword))
        {
            DatabaseErrorMessage = "XPD password is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(XpdMdwPath))
        {
            DatabaseErrorMessage = "MDW path is required for sync.";
            return;
        }

        IsBusy = true;
        SyncInProgress = true;
        SyncProgressMessage = "Starting...";
        SyncProgressPercent = 0;
        DatabaseSuccessMessage = null;
        DatabaseErrorMessage = null;
        try
        {
            string? xpdPath = string.IsNullOrWhiteSpace(XpdFilePath) ? null : XpdFilePath.Trim();
            string? mdwPath = string.IsNullOrWhiteSpace(XpdMdwPath) ? null : XpdMdwPath.Trim();
            string xpdUser = XpdUser.Trim();
            string xpdPassword = XpdPassword;

            JsonElement response = await _apiClient.TriggerSyncAsync(xpdPath, mdwPath, xpdUser, xpdPassword);

            bool started = response.TryGetProperty("success", out JsonElement sE) && sE.GetBoolean();
            if (started)
            {
                DatabaseSuccessMessage = "Sync started. Progress updates below.";
                _ = PollSyncProgressAsync();
            }
            else
            {
                DatabaseErrorMessage = response.TryGetProperty("message", out JsonElement mE) ? mE.GetString() ?? "Sync failed to start" : "Sync failed to start.";
                SyncInProgress = false;
            }
        }
        catch (Exception ex)
        {
            DatabaseErrorMessage = ex.Message;
            SyncInProgress = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PollSyncProgressAsync()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        const int pollMs = 500;
        const int maxPolls = 600;
        int pollCount = 0;
        while (pollCount < maxPolls)
        {
            await Task.Delay(pollMs);
            try
            {
                JsonElement progress = await _apiClient.GetSyncProgressAsync();
                bool inProgress = progress.TryGetProperty("in_progress", out JsonElement ip) && ip.GetBoolean();
                int percent = progress.TryGetProperty("percent", out JsonElement pct) ? pct.GetInt32() : 0;
                string message = progress.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "" : "";
                string stage = progress.TryGetProperty("stage", out JsonElement st) ? st.GetString() ?? "" : "";

                await dispatcher.InvokeAsync(() =>
                {
                    SyncProgressPercent = percent;
                    SyncProgressMessage = string.IsNullOrEmpty(message) ? stage : message;
                });

                if (!inProgress)
                {
                    string finalMessage = string.IsNullOrEmpty(message) ? stage : message;
                    bool isError = string.Equals(stage, "error", StringComparison.OrdinalIgnoreCase)
                        || finalMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                    await dispatcher.InvokeAsync(async () =>
                    {
                        await RefreshSyncStatusAsync();
                        SyncInProgress = false;
                        if (isError)
                        {
                            DatabaseErrorMessage = string.IsNullOrEmpty(finalMessage) ? "Sync failed." : finalMessage;
                            DatabaseSuccessMessage = null;
                        }
                        else if (finalMessage.StartsWith("Sync completed", StringComparison.OrdinalIgnoreCase))
                        {
                            DatabaseSuccessMessage = finalMessage;
                            DatabaseErrorMessage = null;
                        }
                    });
                    return;
                }
            }
            catch (Exception)
            {
                await dispatcher.InvokeAsync(() => SyncInProgress = false);
                return;
            }
            pollCount++;
        }
        await dispatcher.InvokeAsync(() =>
        {
            SyncInProgress = false;
            SyncProgressMessage = "Progress timeout.";
        });
    }

    private async Task RefreshSyncStatusAsync()
    {
        try
        {
            JsonElement status = await _apiClient.GetSyncStatusAsync();
            string lastSync = "Never";
            if (status.TryGetProperty("last_sync", out JsonElement ls) && ls.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string? s = ls.GetString();
                if (!string.IsNullOrEmpty(s))
                    lastSync = s;
            }
            int c = 0, t = 0, items = 0, payments = 0;
            if (status.TryGetProperty("sqlite_counts", out JsonElement sc))
            {
                c = sc.TryGetProperty("customers", out JsonElement ce) ? ce.GetInt32() : 0;
                t = sc.TryGetProperty("tickets", out JsonElement te) ? te.GetInt32() : 0;
                items = sc.TryGetProperty("items", out JsonElement ie) ? ie.GetInt32() : 0;
                payments = sc.TryGetProperty("pawnpayments", out JsonElement pe) ? pe.GetInt32() : 0;
            }
            LastSyncInfo = $"Last sync: {lastSync} — Customers: {c:N0}, Tickets: {t:N0}, Items: {items:N0}, PawnPayments: {payments:N0}";
        }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private async Task LoadStoresAsync()
    {
        try
        {
            JsonElement result = await _apiClient.GetStoresAsync();
            var list = new ObservableCollection<StoreItem>();
            foreach (JsonElement s in result.EnumerateArray())
            {
                int id = s.TryGetProperty("store_id", out JsonElement idE) ? idE.GetInt32() : 0;
                string name = s.TryGetProperty("store_name", out JsonElement nameE) ? nameE.GetString() ?? $"Store {id}" : $"Store {id}";
                int defaultNumId = 0;
                if (s.TryGetProperty("default_number_id", out JsonElement dnE) && dnE.ValueKind == JsonValueKind.Number)
                    defaultNumId = dnE.GetInt32();
                list.Add(new StoreItem { StoreId = id, StoreName = name, DefaultNumberId = defaultNumId });
            }
            AvailableStores = list;
            if (SelectedStoreId == 0 && list.Count > 0)
                SelectedStoreId = list[0].StoreId;
            else
                SelectedStoreId = _appState.CurrentStoreId;
        }
        catch (Exception ex)
        {
            SetError($"Load stores failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveStoreSelectionAsync()
    {
        StoreItem? selected = AvailableStores.FirstOrDefault(s => s.StoreId == SelectedStoreId);
        if (selected is null || SelectedStoreId <= 0) return;

        try
        {
            // Update user's store and Twilio number ID in database
            // Explicitly convert to int? to ensure it's not treated as "not provided"
            int? storeIdToUpdate = SelectedStoreId > 0 ? SelectedStoreId : null;
            int? twilioNumberId = selected.DefaultNumberId > 0 ? selected.DefaultNumberId : _appState.CurrentTwilioNumberId;

            await _apiClient.UpdateProfileAsync(null, storeIdToUpdate, twilioNumberId);

            // Update AppState for current session
            _appState.SetCurrentStore(SelectedStoreId, selected.StoreName, selected.DefaultNumberId);

            // Update CurrentUser in AppState to reflect the new StoreId and TwilioNumberId
            if (_appState.CurrentUser is not null)
            {
                _appState.UpdateCurrentUser(storeIdToUpdate, twilioNumberId);
            }

            StoreSaveMessage = "Store updated successfully.";
            ClearError();
            await LoadPhoneNumbersAsync();
        }
        catch (Exception ex)
        {
            SetError($"Failed to update store: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddStoreAsync()
    {
        if (string.IsNullOrWhiteSpace(NewStoreName))
        {
            SetError("Enter a store name.");
            return;
        }
        await AddStoreByNameAsync(NewStoreName);
        NewStoreName = string.Empty;
    }

    /// <summary>Create a new store by name. Called from AddStoreDialog.</summary>
    public async Task AddStoreByNameAsync(string storeName)
    {
        if (string.IsNullOrWhiteSpace(storeName))
        {
            SetError("Enter a store name.");
            return;
        }

        try
        {
            JsonElement result = await _apiClient.CreateStoreAsync(storeName.Trim());
            int newId = result.TryGetProperty("store_id", out JsonElement idE) ? idE.GetInt32() : 0;
            string newName = result.TryGetProperty("store_name", out JsonElement nameE) ? nameE.GetString() ?? storeName.Trim() : storeName.Trim();
            ClearError();
            _appState.SetCurrentStore(newId, newName, 0);
            OnPropertyChanged(nameof(CurrentStoreDisplay));
            await LoadStoresAsync();
            SelectedStoreId = newId;
            await LoadPhoneNumbersAsync();
            StoreSaveMessage = $"Store \"{newName}\" created. You're now using it.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message.Contains("403") ? "Only HQ users can create stores." : $"Add store failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadPhoneNumbersAsync()
    {
        try
        {
            // Use SelectedStoreId if available, otherwise fall back to CurrentStoreId
            int storeIdToUse = SelectedStoreId > 0 ? SelectedStoreId : _appState.CurrentStoreId;
            
            // If still no store ID, try to get from AvailableStores
            if (storeIdToUse <= 0 && AvailableStores.Count > 0)
            {
                storeIdToUse = AvailableStores[0].StoreId;
                SelectedStoreId = storeIdToUse;
            }
            
            if (storeIdToUse <= 0)
            {
                PhoneNumbers = new ObservableCollection<TwilioNumberItem>();
                return;
            }

            JsonElement result = await _apiClient.GetTwilioNumbersAsync(storeIdToUse);
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
            int storeIdToUse = SelectedStoreId > 0 ? SelectedStoreId : _appState.CurrentStoreId;
            if (storeIdToUse <= 0)
            {
                SetError("No store selected. Please select a store first.");
                return;
            }

            await _apiClient.AddNumberAsync(storeIdToUse, NewPhoneNumber);
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
            int storeIdToUse = SelectedStoreId > 0 ? SelectedStoreId : _appState.CurrentStoreId;
            if (storeIdToUse <= 0)
            {
                SetError("No store selected. Please select a store first.");
                return;
            }

            await _apiClient.SetDefaultNumberAsync(storeIdToUse, numberId);
            
            // Update user's TwilioNumberId in database
            int? userStoreId = _appState.CurrentUser?.StoreId;
            await _apiClient.UpdateProfileAsync(null, userStoreId, numberId);
            
            // Update AppState so the app uses this number for SMS, VIP, etc.
            _appState.CurrentTwilioNumberId = numberId;
            
            // Update CurrentUser in AppState
            if (_appState.CurrentUser is not null)
            {
                _appState.UpdateCurrentUser(_appState.CurrentUser.StoreId, numberId);
            }
            
            // Reload phone numbers to refresh the UI (show updated default badge)
            await LoadPhoneNumbersAsync();
            ClearError();
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
            int storeIdToUse = SelectedStoreId > 0 ? SelectedStoreId : _appState.CurrentStoreId;
            if (storeIdToUse <= 0)
            {
                SetError("No store selected. Please select a store first.");
                return;
            }

            await _apiClient.DeleteNumberAsync(storeIdToUse, numberId);
            await LoadPhoneNumbersAsync();
        }
        catch (Exception ex)
        {
            SetError($"Delete number failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveTwilioConfigAsync()
    {
        try
        {
            _twilioConfig.Save(TwilioSid, TwilioToken);
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

}
