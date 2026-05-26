using System.Collections.ObjectModel;
using System.Diagnostics;
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

// Review channel display item.
public sealed class ReviewChannelItem
{
    public int ReviewChannelId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ReviewUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

// Review history display item.
public sealed class ReviewHistoryItem
{
    public int ReviewRequestId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string SentAtText => SentAt.ToString("MMM d, yyyy  h:mm tt");
}

// Settings ViewModel with 8 tabs: Credentials, Database, Phone Numbers, Twilio, Reminders, VoIP, Reviews (channels + automation), Quality.
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly AppState _appState;
    private readonly TwilioConfigService _twilioConfig;
    private readonly XBlueService _xblueService;
    private readonly XBlueConfigService _xblueConfig;
    private bool _suspendXBluePersist;

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

    /// <summary>True when the API confirms the password is persisted (we don't show the value).</summary>
    [ObservableProperty]
    private bool _xpdPasswordPersisted;

    [ObservableProperty]
    private bool _databaseConfigSaving;

    [ObservableProperty]
    private string _databaseConfigSaveMessage = string.Empty;

    /// <summary>"ready" / "blocked" / "unknown" — drives the colored banner on the Database tab.</summary>
    [ObservableProperty]
    private string _preflightStatus = "unknown";

    [ObservableProperty]
    private string _preflightSummary = "Tap “Pre-flight check” to verify this PC can sync.";

    [ObservableProperty]
    private ObservableCollection<string> _preflightBlockers = new();

    [ObservableProperty]
    private bool _preflightBusy;

    [ObservableProperty]
    private string _autoSyncStatusMessage = string.Empty;

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

    [ObservableProperty]
    private string _twilioMessagingServiceSid = string.Empty;

    /// <summary>"live" or "mock" — drives the colored banner on the Twilio settings tab.</summary>
    [ObservableProperty]
    private string _twilioMode = "unknown";

    /// <summary>Human-readable banner message for the Twilio tab.</summary>
    [ObservableProperty]
    private string _twilioStatusMessage = "Checking Twilio status…";

    /// <summary>True when the API reports it is in mock mode (outbound SMS not delivered).</summary>
    [ObservableProperty]
    private bool _twilioIsMock;

    [ObservableProperty]
    private string _twilioSaveMessage = string.Empty;

    [ObservableProperty]
    private bool _twilioSaveBusy;

    // Tab 4: Reminders
    [ObservableProperty]
    private bool _schedulerRunning;

    [ObservableProperty]
    private string _schedulerNextRun = string.Empty;

    [ObservableProperty]
    private int _dailySent;

    [ObservableProperty]
    private int _dailyLimit;

    [ObservableProperty]
    private string _runNowResult = string.Empty;

    [ObservableProperty]
    private bool _runNowBusy;

    [ObservableProperty]
    private bool _reminderRunActive;

    [ObservableProperty]
    private int _reminderProgressSent;

    [ObservableProperty]
    private int _reminderProgressFailed;

    [ObservableProperty]
    private int _reminderProgressSkipped;

    [ObservableProperty]
    private int _reminderProgressTotal;

    [ObservableProperty]
    private int _reminderProgressProcessed;

    [ObservableProperty]
    private string _reminderProgressPhase = string.Empty;

    // Tab 5: VoIP
    [ObservableProperty]
    private string _xblueIp = string.Empty;

    [ObservableProperty]
    private bool _xblueEnabled;

    [ObservableProperty]
    private bool _xblueSpeakerBeforeDial = true;

    [ObservableProperty]
    private bool _xbluePressPoundToSend;

    [ObservableProperty]
    private string _xblueOutboundPrefix = string.Empty;

    [ObservableProperty]
    private string _xblueUsername = string.Empty;

    [ObservableProperty]
    private string _xbluePassword = string.Empty;

    [ObservableProperty]
    private string _xblueVoipTestSummary = string.Empty;

    [ObservableProperty]
    private bool _xblueVoipTestBusy;

    [ObservableProperty]
    private string _xblueDialTarget = "7185649706";

    // Tab 6: Reviews
    [ObservableProperty]
    private ObservableCollection<ReviewChannelItem> _reviewChannels = new();

    [ObservableProperty]
    private ObservableCollection<ReviewHistoryItem> _reviewHistory = new();

    [ObservableProperty]
    private bool _reviewHistoryLoading;

    [ObservableProperty]
    private bool _hasMoreHistory;

    private const int HistoryPageSize = 20;

    [ObservableProperty]
    private string _newPlatformName = "Google";

    [ObservableProperty]
    private string _newReviewUrl = string.Empty;

    [ObservableProperty]
    private string _reviewErrorMessage = string.Empty;

    [ObservableProperty]
    private string _reviewSuccessMessage = string.Empty;

    [ObservableProperty]
    private bool _reviewAutoEnabled;

    [ObservableProperty]
    private int _reviewAutoIntervalMinutes = 30;

    [ObservableProperty]
    private bool _reviewAutoRunOnStartup;

    [ObservableProperty]
    private bool _reviewAutoTimerRunning;

    [ObservableProperty]
    private string _reviewAutoSettingsPath = string.Empty;

    [ObservableProperty]
    private string _reviewAutoStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _reviewAutoBusy;

    // Tab 7: Quality
    private CustomerQualityQueryService? _qualityQueryService;

    [ObservableProperty]
    private string _qualityQuery = string.Empty;

    [ObservableProperty]
    private string _qualitySaveMessage = string.Empty;

    [ObservableProperty]
    private string _qualityErrorMessage = string.Empty;

    // Tab N (added in M4): HQ Hub connection
    [ObservableProperty]
    private bool _hubEnabled;

    [ObservableProperty]
    private string _hubUrl = string.Empty;

    [ObservableProperty]
    private string _hubStoreKey = string.Empty;

    [ObservableProperty]
    private string _hubDeploymentId = string.Empty;

    [ObservableProperty]
    private int _hubIntervalSeconds = 60;

    [ObservableProperty]
    private bool _showHubStoreKey;

    [ObservableProperty]
    private bool _hubSaveBusy;

    [ObservableProperty]
    private bool _hubTestBusy;

    [ObservableProperty]
    private string _hubSaveMessage = string.Empty;

    [ObservableProperty]
    private string _hubTestMessage = string.Empty;

    [ObservableProperty]
    private bool _hubTestSuccess;

    [ObservableProperty]
    private string _hubConfigPath = string.Empty;

    private readonly HubConfigService? _hubConfig;

    public SettingsViewModel(ApiClient apiClient, AppState appState, TwilioConfigService twilioConfig,
        XBlueService xblueService, XBlueConfigService xblueConfig,
        CustomerQualityQueryService? qualityQueryService = null,
        HubConfigService? hubConfig = null)
    {
        _apiClient = apiClient;
        _appState = appState;
        _twilioConfig = twilioConfig;
        _xblueService = xblueService;
        _xblueConfig = xblueConfig;
        _qualityQueryService = qualityQueryService;
        _hubConfig = hubConfig;
        SelectedStoreId = _appState.CurrentStoreId;
        LoadTwilioConfigFromFile();
        LoadXBlueIntoViewModel();
        LoadHubConfigFromFile();
    }

    private void LoadHubConfigFromFile()
    {
        if (_hubConfig is null) return;
        HubConfigService.HubConfigModel m = _hubConfig.Load();
        HubEnabled = m.Enabled;
        HubUrl = m.Url;
        HubStoreKey = m.StoreKey;
        HubDeploymentId = m.DeploymentId;
        HubIntervalSeconds = m.IntervalSeconds <= 0 ? 60 : m.IntervalSeconds;
        HubConfigPath = _hubConfig.ConfigFilePath;
    }

    [RelayCommand]
    private async Task SaveHubConfig()
    {
        if (_hubConfig is null) return;
        HubSaveBusy = true;
        HubSaveMessage = string.Empty;
        try
        {
            // Trim everything that isn't intentionally whitespace. The store key
            // is base64-ish (no leading/trailing whitespace ever expected).
            HubConfigService.HubConfigModel m = new()
            {
                Enabled = HubEnabled,
                Url = (HubUrl ?? string.Empty).Trim(),
                StoreKey = (HubStoreKey ?? string.Empty).Trim(),
                DeploymentId = (HubDeploymentId ?? string.Empty).Trim(),
                IntervalSeconds = HubIntervalSeconds <= 0 ? 60 : HubIntervalSeconds
            };
            await Task.Run(() => _hubConfig.Save(m));
            HubSaveMessage = "Saved. Restart the application for the new settings to take effect.";
        }
        catch (Exception ex)
        {
            HubSaveMessage = "Save failed: " + ex.Message;
        }
        finally
        {
            HubSaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestHubConnection()
    {
        HubTestBusy = true;
        HubTestMessage = string.Empty;
        HubTestSuccess = false;
        try
        {
            string url = (HubUrl ?? string.Empty).Trim().TrimEnd('/');
            string key = (HubStoreKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                HubTestMessage = "Enter Hub URL and Store Key first.";
                return;
            }

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Store-Key", key);
            using System.Net.Http.HttpResponseMessage response = await http.GetAsync($"{url}/api/heartbeat/echo");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                string storeName = doc.RootElement.TryGetProperty("store_name", out JsonElement n)
                    ? n.GetString() ?? "(unknown)"
                    : "(unknown)";
                int storeId = doc.RootElement.TryGetProperty("store_id", out JsonElement i) && i.TryGetInt32(out int sid) ? sid : 0;
                HubTestMessage = $"OK -- Hub recognized this key as store '{storeName}' (id {storeId}).";
                HubTestSuccess = true;
            }
            else if ((int)response.StatusCode == 401)
            {
                HubTestMessage = "Failed: Hub rejected the Store Key (401). Re-copy the key from HQ.";
            }
            else
            {
                HubTestMessage = $"Failed: Hub returned HTTP {(int)response.StatusCode}.";
            }
        }
        catch (Exception ex)
        {
            HubTestMessage = "Failed: " + ex.Message;
        }
        finally
        {
            HubTestBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleShowHubStoreKey() => ShowHubStoreKey = !ShowHubStoreKey;

    private void LoadXBlueIntoViewModel()
    {
        _suspendXBluePersist = true;
        XBlueSettings s = _xblueConfig.Load();
        XblueIp = s.Ip;
        XblueEnabled = s.Enabled;
        XblueSpeakerBeforeDial = s.SpeakerBeforeDial;
        XbluePressPoundToSend = s.PressPoundToSend;
        XblueOutboundPrefix = s.OutboundPrefix;
        XblueUsername = s.Username;
        XbluePassword = s.Password;
        ApplyXblueToService();
        _suspendXBluePersist = false;
    }

    private void ApplyXblueToService()
    {
        _xblueService.Configure(
            XblueIp.Trim(),
            XblueEnabled,
            XblueUsername?.Trim() ?? "",
            XbluePassword ?? "",
            XblueSpeakerBeforeDial,
            XblueOutboundPrefix ?? "",
            XbluePressPoundToSend);
    }

    partial void OnXblueIpChanged(string value) => PersistXBlueIfNeeded();

    partial void OnXblueEnabledChanged(bool value) => PersistXBlueIfNeeded();

    partial void OnXblueSpeakerBeforeDialChanged(bool value) => PersistXBlueIfNeeded();

    partial void OnXbluePressPoundToSendChanged(bool value) => PersistXBlueIfNeeded();

    partial void OnXblueOutboundPrefixChanged(string value) => PersistXBlueIfNeeded();

    partial void OnXblueUsernameChanged(string value) => PersistXBlueIfNeeded();

    partial void OnXbluePasswordChanged(string value) => PersistXBlueIfNeeded();

    private void PersistXBlueIfNeeded()
    {
        if (_suspendXBluePersist) return;
        ApplyXblueToService();
        _xblueConfig.Save(
            XblueIp,
            XblueEnabled,
            XblueUsername?.Trim() ?? "",
            XbluePassword ?? "",
            XblueSpeakerBeforeDial,
            XblueOutboundPrefix ?? "",
            XbluePressPoundToSend);
    }

    [RelayCommand]
    private void OpenFanvilWebUi()
    {
        XblueVoipTestSummary = string.Empty;
        string ip = XblueIp?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(ip))
        {
            XblueVoipTestSummary = "Enter the phone IP address first.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://{ip}/",
                UseShellExecute = true
            });
            XblueVoipTestSummary =
                "Opened the phone in your default browser. Walk the left menu tab by tab with support — share a screenshot of any screen if something is unclear.";
        }
        catch (Exception ex)
        {
            XblueVoipTestSummary = "Could not open browser: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestVoipConnectionAsync()
    {
        XblueVoipTestSummary = string.Empty;
        XblueVoipTestBusy = true;
        try
        {
            ApplyXblueToService();
            XBlueConnectionTest result = await _xblueService.TestConnectionAsync(XblueIp?.Trim());
            XblueVoipTestSummary = result.Message;
        }
        finally
        {
            XblueVoipTestBusy = false;
        }
    }

    [RelayCommand]
    private async Task VoipVolumeUpAsync()
    {
        XblueVoipTestSummary = string.Empty;

        if (string.IsNullOrWhiteSpace(XblueIp))
        {
            XblueVoipTestSummary = "Enter the phone IP address first.";
            return;
        }

        if (!XblueEnabled)
        {
            XblueVoipTestSummary = "Enable XBlue VoIP to send volume commands to the phone.";
            return;
        }

        ApplyXblueToService();
        XblueVoipTestBusy = true;
        try
        {
            bool ok = await _xblueService.VolumeUpAsync();
            XblueVoipTestSummary = ok
                ? "Volume up command sent — you should hear the ring or call volume step up on the phone."
                : "Volume command did not get a success response. Check user/password and that ConfigManApp.com accepts Basic auth.";
        }
        finally
        {
            XblueVoipTestBusy = false;
        }
    }

    [RelayCommand]
    private async Task VoipSpeakerAsync()
    {
        XblueVoipTestSummary = string.Empty;

        if (string.IsNullOrWhiteSpace(XblueIp))
        {
            XblueVoipTestSummary = "Enter the phone IP address first.";
            return;
        }

        if (!XblueEnabled)
        {
            XblueVoipTestSummary = "Enable XBlue VoIP to control the phone.";
            return;
        }

        ApplyXblueToService();
        XblueVoipTestBusy = true;
        try
        {
            bool ok = await _xblueService.ToggleSpeakerAsync();
            XblueVoipTestSummary = ok
                ? "Speaker command sent — toggles speakerphone / handsfree on the phone (press again to turn off)."
                : "Speaker command failed. Check credentials and phone API.";
        }
        finally
        {
            XblueVoipTestBusy = false;
        }
    }

    [RelayCommand]
    private async Task VoipUnmuteAsync()
    {
        XblueVoipTestSummary = string.Empty;

        if (string.IsNullOrWhiteSpace(XblueIp))
        {
            XblueVoipTestSummary = "Enter the phone IP address first.";
            return;
        }

        if (!XblueEnabled)
        {
            XblueVoipTestSummary = "Enable XBlue VoIP to control the phone.";
            return;
        }

        ApplyXblueToService();
        XblueVoipTestBusy = true;
        try
        {
            bool ok = await _xblueService.ToggleMuteAsync();
            XblueVoipTestSummary = ok
                ? "Mute toggle sent — if you were muted, audio should be back on (press again to mute)."
                : "Mute command failed. Check credentials and phone API.";
        }
        finally
        {
            XblueVoipTestBusy = false;
        }
    }

    [RelayCommand]
    private async Task VoipDialAsync()
    {
        XblueVoipTestSummary = string.Empty;

        if (string.IsNullOrWhiteSpace(XblueIp))
        {
            XblueVoipTestSummary = "Enter the phone IP address first.";
            return;
        }

        if (!XblueEnabled)
        {
            XblueVoipTestSummary = "Enable XBlue VoIP to dial from the app.";
            return;
        }

        string target = XblueDialTarget?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(target))
        {
            XblueVoipTestSummary = "Enter an extension or phone number to dial.";
            return;
        }

        ApplyXblueToService();
        XblueVoipTestBusy = true;
        try
        {
            XBlueDialResult r = await _xblueService.DialAsync(target);
            XblueVoipTestSummary = r.Ok
                ? $"Dial “{target}”: {r.Message}"
                : (r.StatusCode > 0
                    ? $"Dial failed (HTTP {r.StatusCode}): {r.Message}"
                    : r.Message);
        }
        finally
        {
            XblueVoipTestBusy = false;
        }
    }

    private void LoadTwilioConfigFromFile()
    {
        TwilioConfigService.TwilioConfigModel m = _twilioConfig.Load();
        TwilioSid = m.AccountSid ?? string.Empty;
        TwilioToken = m.AuthToken ?? string.Empty;
        TwilioMessagingServiceSid = m.MessagingServiceSid ?? string.Empty;
    }

    /// <summary>
    /// Asks the API whether it sees real Twilio credentials. Updates the banner shown
    /// at the top of the Twilio settings tab. Safe to call on every tab switch.
    /// </summary>
    [RelayCommand]
    private async Task RefreshTwilioStatusAsync()
    {
        try
        {
            TwilioStatusInfo? status = await _apiClient.GetTwilioStatusAsync();
            if (status is null)
            {
                TwilioMode = "unknown";
                TwilioIsMock = false;
                TwilioStatusMessage = "Could not reach the API. Twilio status is unknown.";
                return;
            }

            TwilioMode = status.Mode;
            TwilioIsMock = status.Mock;
            if (status.Mock)
            {
                TwilioStatusMessage = string.IsNullOrWhiteSpace(status.Warning)
                    ? "Twilio is in MOCK mode — outbound SMS is NOT being delivered."
                    : status.Warning;
            }
            else
            {
                string sidNote = string.IsNullOrEmpty(status.AccountSidPrefix)
                    ? string.Empty
                    : $" (Account SID {status.AccountSidPrefix}…)";
                string msNote = status.HasMessagingService ? " · Messaging Service configured" : string.Empty;
                TwilioStatusMessage = $"Twilio is LIVE{sidNote}{msNote}. Outbound SMS will reach customers.";
            }
        }
        catch (Exception ex)
        {
            TwilioMode = "unknown";
            TwilioIsMock = false;
            TwilioStatusMessage = $"Twilio status check failed: {ex.Message}";
        }
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

            // Don't surface the password value, just whether one is set, so the
            // operator can see "*** saved" without the API leaking the password.
            XpdPasswordPersisted = config.TryGetProperty("xpd_password_set", out JsonElement pw) && pw.GetBoolean();

            await RefreshSyncStatusAsync();
            await LoadPreflightAsync();
        }
        catch (Exception ex)
        {
            DatabaseErrorMessage = $"Could not load config: {ex.Message}";
        }
    }

    /// <summary>
    /// Persist the path + credentials on the API so the hourly auto-sync uses
    /// them too (not just per-run manual sync). This closes the previous
    /// behavior where saving in this UI only affected manual sync.
    /// </summary>
    [RelayCommand]
    private async Task SaveDatabaseConfigAsync()
    {
        DatabaseConfigSaveMessage = string.Empty;
        DatabaseErrorMessage = string.Empty;
        DatabaseSuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(XpdFilePath))
        {
            DatabaseErrorMessage = "XPD file path is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(XpdMdwPath))
        {
            DatabaseErrorMessage = "MDW path is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(XpdUser))
        {
            DatabaseErrorMessage = "XPD user is required.";
            return;
        }

        DatabaseConfigSaving = true;
        try
        {
            // Empty password preserves the previously-saved password on the
            // API side -- so the operator can re-save other fields without
            // re-typing the password every time.
            string? passwordToSend = string.IsNullOrEmpty(XpdPassword) ? null : XpdPassword;

            JsonElement response = await _apiClient.SaveSyncConfigAsync(
                XpdFilePath.Trim(),
                XpdMdwPath.Trim(),
                XpdUser.Trim(),
                passwordToSend);

            XpdPasswordPersisted = response.TryGetProperty("xpd_password_set", out JsonElement pw) && pw.GetBoolean();
            DatabaseConfigSaveMessage = "Saved. Manual and automatic sync will use these settings.";

            await LoadPreflightAsync();
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            DatabaseConfigSaveMessage = msg.Contains("403")
                ? "Only HQ users can change sync configuration."
                : $"Save failed: {msg}";
            DatabaseErrorMessage = DatabaseConfigSaveMessage;
        }
        finally
        {
            DatabaseConfigSaving = false;
        }
    }

    /// <summary>
    /// Run all server-side checks (file exists, MDW present, store row,
    /// scheduler state) and produce a single ready/blocked banner for the UI.
    /// </summary>
    [RelayCommand]
    private async Task LoadPreflightAsync()
    {
        PreflightBusy = true;
        try
        {
            JsonElement pf = await _apiClient.GetSyncPreflightAsync();

            bool ready = pf.TryGetProperty("ready", out JsonElement readyEl) && readyEl.GetBoolean();
            PreflightStatus = ready ? "ready" : "blocked";

            ObservableCollection<string> blockers = new();
            if (pf.TryGetProperty("blockers", out JsonElement bl) && bl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement b in bl.EnumerateArray())
                {
                    string? msg = b.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        blockers.Add(msg);
                }
            }
            PreflightBlockers = blockers;

            if (ready)
            {
                long size = pf.TryGetProperty("xpd_file_size", out JsonElement sz) && sz.ValueKind == JsonValueKind.Number
                    ? sz.GetInt64() : 0;
                string sizeText = size > 0 ? $" ({size / (1024.0 * 1024.0):F1} MB)" : "";
                string modText = "";
                if (pf.TryGetProperty("xpd_file_modified", out JsonElement mod) && mod.ValueKind == JsonValueKind.String)
                {
                    string? modStr = mod.GetString();
                    if (!string.IsNullOrEmpty(modStr) && DateTime.TryParse(modStr, out DateTime modDt))
                        modText = $", last modified {modDt:MMM d  h:mm tt}";
                }
                PreflightSummary = $"Ready to sync. XPD file found{sizeText}{modText}.";
            }
            else
            {
                PreflightSummary = blockers.Count == 1
                    ? blockers[0]
                    : $"{blockers.Count} prerequisite{(blockers.Count == 1 ? "" : "s")} blocking sync — see list below.";
            }

            // Auto-sync banner: surfaces scheduler state alongside file health
            bool schedRunning = pf.TryGetProperty("scheduler_running", out JsonElement sr) && sr.GetBoolean();
            string? nextRun = pf.TryGetProperty("scheduler_next_run", out JsonElement nr) && nr.ValueKind == JsonValueKind.String
                ? nr.GetString() : null;
            bool? lastSuccess = pf.TryGetProperty("scheduler_last_success", out JsonElement ls) && ls.ValueKind != JsonValueKind.Null
                ? ls.GetBoolean() : (bool?)null;
            string? lastError = pf.TryGetProperty("scheduler_last_error", out JsonElement le) && le.ValueKind == JsonValueKind.String
                ? le.GetString() : null;

            if (!schedRunning)
            {
                AutoSyncStatusMessage = "Auto-sync is OFF.";
            }
            else if (lastSuccess == false && !string.IsNullOrWhiteSpace(lastError))
            {
                AutoSyncStatusMessage = $"Auto-sync ON — last run FAILED: {lastError}";
            }
            else
            {
                string nextText = string.IsNullOrWhiteSpace(nextRun) ? "" : $", next at {nextRun}";
                AutoSyncStatusMessage = $"Auto-sync ON{nextText}.";
            }
        }
        catch (Exception ex)
        {
            PreflightStatus = "unknown";
            PreflightSummary = $"Could not run pre-flight check: {ex.Message}";
            PreflightBlockers = new ObservableCollection<string>();
            AutoSyncStatusMessage = string.Empty;
        }
        finally
        {
            PreflightBusy = false;
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
        DatabaseSuccessMessage = string.Empty;
        DatabaseErrorMessage = string.Empty;
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
                            DatabaseSuccessMessage = string.Empty;
                        }
                        else if (finalMessage.StartsWith("Sync completed", StringComparison.OrdinalIgnoreCase))
                        {
                            DatabaseSuccessMessage = finalMessage;
                            DatabaseErrorMessage = string.Empty;
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
    private async Task SaveTwilioConfigAsync()
    {
        TwilioSaveMessage = string.Empty;
        TwilioSaveBusy = true;
        try
        {
            _twilioConfig.Save(
                TwilioSid?.Trim() ?? string.Empty,
                TwilioToken ?? string.Empty,
                TwilioMessagingServiceSid?.Trim() ?? string.Empty);

            TwilioSaveMessage = "Saved. Verifying with the API…";
            // Give the API a moment, then re-check live/mock status. Because
            // TwilioService now resolves IOptionsSnapshot, the new credentials
            // are picked up on the very next request — no API restart required.
            await Task.Delay(300);
            await RefreshTwilioStatusAsync();

            TwilioSaveMessage = TwilioIsMock
                ? "Saved, but the API still reports MOCK mode. Double-check the Account SID and Auth Token, then save again."
                : "Saved. Twilio is LIVE — outbound SMS will reach customers.";
        }
        catch (Exception ex)
        {
            TwilioSaveMessage = $"Save failed: {ex.Message}";
            SetError($"Save Twilio config failed: {ex.Message}");
        }
        finally
        {
            TwilioSaveBusy = false;
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
                ApplyRunProgress(status);

                if (status.IsRunInProgress && !RunNowBusy)
                {
                    RunNowBusy = true;
                    RunNowResult = string.Empty;
                    _ = PollRunProgressAsync();
                }
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
    private async Task RunRemindersNowAsync()
    {
        RunNowResult = string.Empty;
        RunNowBusy = true;
        ReminderRunActive = true;
        ReminderProgressSent = 0;
        ReminderProgressFailed = 0;
        ReminderProgressSkipped = 0;
        ReminderProgressTotal = 0;
        ReminderProgressProcessed = 0;
        ReminderProgressPhase = "Starting...";

        try
        {
            await _apiClient.RunAutoRemindersAsync();
            await PollRunProgressAsync();
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            if (msg.Contains("409") || msg.Contains("Conflict") || msg.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
            {
                RunNowResult = "A run is already in progress.";
                await PollRunProgressAsync();
            }
            else
            {
                RunNowResult = $"Error: {msg}";
                ReminderRunActive = false;
                RunNowBusy = false;
            }
        }
    }

    private async Task PollRunProgressAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(2000);
                SchedulerStatus? status = await _apiClient.GetSchedulerStatusAsync();
                if (status is null) break;

                ApplyRunProgress(status);

                if (!status.IsRunInProgress)
                {
                    RunNowResult = status.RunSent > 0 || status.RunFailed > 0
                        ? $"Done: {status.RunSent} sent, {status.RunFailed} failed, {status.RunSkipped} skipped"
                        : "Done: No eligible tickets found for today's reminder dates.";

                    DailySent = status.DailySent;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            RunNowResult = $"Polling error: {ex.Message}";
        }
        finally
        {
            ReminderRunActive = false;
            RunNowBusy = false;
            await LoadSchedulerStatusAsync();
        }
    }

    private void ApplyRunProgress(SchedulerStatus status)
    {
        ReminderRunActive = status.IsRunInProgress;
        ReminderProgressSent = status.RunSent;
        ReminderProgressFailed = status.RunFailed;
        ReminderProgressSkipped = status.RunSkipped;
        ReminderProgressTotal = status.RunTotalEligible;
        ReminderProgressProcessed = status.RunSent + status.RunFailed + status.RunSkipped;
        ReminderProgressPhase = status.RunCurrentPhase ?? string.Empty;
    }

    [RelayCommand]
    private void LoadQualityQuery()
    {
        QualitySaveMessage = string.Empty;
        QualityErrorMessage = string.Empty;
        if (_qualityQueryService is null)
        {
            QualityErrorMessage = "Quality query service not available.";
            return;
        }
        QualityQuery = _qualityQueryService.LoadQuery();
    }

    [RelayCommand]
    private void SaveQualityQuery()
    {
        QualitySaveMessage = string.Empty;
        QualityErrorMessage = string.Empty;
        if (_qualityQueryService is null)
        {
            QualityErrorMessage = "Quality query service not available.";
            return;
        }
        if (string.IsNullOrWhiteSpace(QualityQuery))
        {
            QualityErrorMessage = "Query cannot be empty.";
            return;
        }
        try
        {
            _qualityQueryService.SaveQuery(QualityQuery);
            QualitySaveMessage = "Quality query saved. Changes apply on next customer load.";
        }
        catch (Exception ex)
        {
            QualityErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetQualityQuery()
    {
        QualitySaveMessage = string.Empty;
        QualityErrorMessage = string.Empty;
        if (_qualityQueryService is null) return;
        QualityQuery = _qualityQueryService.GetDefaultQuery();
        QualitySaveMessage = "Reset to default. Click Save to persist.";
    }

    // ── Reviews tab ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadReviewAutomationAsync()
    {
        ReviewAutoStatusMessage = string.Empty;
        try
        {
            JsonElement s = await _apiClient.GetReviewAutomationSettingsAsync();
            ReviewAutoEnabled = s.TryGetProperty("enabled", out JsonElement en) && en.GetBoolean();
            ReviewAutoIntervalMinutes = s.TryGetProperty("intervalMinutes", out JsonElement im) ? im.GetInt32() : 30;
            if (ReviewAutoIntervalMinutes < 1) ReviewAutoIntervalMinutes = 30;
            ReviewAutoRunOnStartup = s.TryGetProperty("runOnStartup", out JsonElement rs) && rs.GetBoolean();
            await LoadReviewAutomationStatusAsync();
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Review automation settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadReviewAutomationStatusAsync()
    {
        try
        {
            JsonElement st = await _apiClient.GetReviewAutomationStatusAsync();
            ReviewAutoTimerRunning = st.TryGetProperty("schedulerRunning", out JsonElement sr) && sr.GetBoolean();
            ReviewAutoSettingsPath = st.TryGetProperty("settingsFilePath", out JsonElement p) ? p.GetString() ?? "" : "";
        }
        catch
        {
            // Status is optional if API is older.
        }
    }

    [RelayCommand]
    private async Task SaveReviewAutomationAsync()
    {
        ReviewAutoStatusMessage = string.Empty;
        ReviewErrorMessage = string.Empty;
        if (ReviewAutoIntervalMinutes < 1 || ReviewAutoIntervalMinutes > 1440)
        {
            ReviewAutoStatusMessage = "Interval must be between 1 and 1440 minutes.";
            return;
        }

        ReviewAutoBusy = true;
        try
        {
            await _apiClient.PutReviewAutomationSettingsAsync(
                ReviewAutoEnabled, ReviewAutoIntervalMinutes, ReviewAutoRunOnStartup);
            ReviewAutoStatusMessage = "Saved. The API review automation timer was updated.";
            await LoadReviewAutomationStatusAsync();
        }
        catch (Exception ex)
        {
            ReviewAutoStatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            ReviewAutoBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunReviewAutomationNowAsync()
    {
        ReviewAutoStatusMessage = string.Empty;
        ReviewAutoBusy = true;
        try
        {
            JsonElement r = await _apiClient.RunReviewAutomationNowAsync();
            int sent = r.TryGetProperty("sent", out JsonElement s) ? s.GetInt32() : 0;
            int failed = r.TryGetProperty("failed", out JsonElement f) ? f.GetInt32() : 0;
            int skipped = r.TryGetProperty("skipped", out JsonElement sk) ? sk.GetInt32() : 0;
            string? detail = r.TryGetProperty("detail", out JsonElement d) ? d.GetString() : null;

            ReviewAutoStatusMessage = detail switch
            {
                "run_already_in_progress" => "A run is already in progress.",
                "bootstrap" => "Initialized watermark (no messages sent).",
                "no_new_tickets" => "No new tickets since last check.",
                _ => $"Done: {sent} sent, {failed} failed, {skipped} skipped."
            };
        }
        catch (Exception ex)
        {
            ReviewAutoStatusMessage = ex.Message.Contains("409", StringComparison.Ordinal) || ex.Message.Contains("Conflict", StringComparison.Ordinal)
                ? "A run is already in progress."
                : $"Run failed: {ex.Message}";
        }
        finally
        {
            ReviewAutoBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadReviewChannelsAsync()
    {
        ReviewErrorMessage = string.Empty;
        ReviewSuccessMessage = string.Empty;

        try
        {
            int storeId = _appState.CurrentStoreId;
            if (storeId == 0)
            {
                ReviewErrorMessage = "No store selected.";
                return;
            }

            JsonElement result = await _apiClient.GetReviewChannelsAsync(storeId);
            ObservableCollection<ReviewChannelItem> items = new();

            foreach (JsonElement ch in result.EnumerateArray())
            {
                items.Add(new ReviewChannelItem
                {
                    ReviewChannelId = ch.TryGetProperty("reviewChannelId", out var idE) ? idE.GetInt32() : 0,
                    PlatformName = ch.TryGetProperty("platformName", out var pnE) ? pnE.GetString() ?? "" : "",
                    ReviewUrl = ch.TryGetProperty("reviewUrl", out var urlE) ? urlE.GetString() ?? "" : "",
                    SortOrder = ch.TryGetProperty("sortOrder", out var soE) ? soE.GetInt32() : 0,
                    IsActive = !ch.TryGetProperty("isActive", out var iaE) || iaE.GetBoolean()
                });
            }

            ReviewChannels = items;
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to load channels: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddReviewChannelAsync()
    {
        ReviewErrorMessage = string.Empty;
        ReviewSuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(NewPlatformName))
        {
            ReviewErrorMessage = "Platform name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewReviewUrl))
        {
            ReviewErrorMessage = "Review URL is required.";
            return;
        }

        try
        {
            int storeId = _appState.CurrentStoreId;
            int sortOrder = ReviewChannels.Count;

            await _apiClient.CreateReviewChannelAsync(storeId, NewPlatformName.Trim(), NewReviewUrl.Trim(), sortOrder);

            NewReviewUrl = string.Empty;
            ReviewSuccessMessage = $"Added {NewPlatformName} channel.";
            await LoadReviewChannelsAsync();
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to add channel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteReviewChannelAsync(int channelId)
    {
        ReviewErrorMessage = string.Empty;
        ReviewSuccessMessage = string.Empty;

        try
        {
            await _apiClient.DeleteReviewChannelAsync(channelId);
            ReviewSuccessMessage = "Channel deleted.";
            await LoadReviewChannelsAsync();
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to delete channel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadReviewHistoryAsync()
    {
        try
        {
            int storeId = _appState.CurrentStoreId;
            if (storeId == 0) return;

            ReviewHistoryLoading = true;
            JsonElement result = await _apiClient.GetReviewHistoryAsync(storeId, 0, HistoryPageSize + 1);
            ObservableCollection<ReviewHistoryItem> items = new();

            int count = 0;
            foreach (JsonElement r in result.EnumerateArray())
            {
                count++;
                if (count > HistoryPageSize) break;
                items.Add(ParseHistoryItem(r));
            }

            ReviewHistory = items;
            HasMoreHistory = count > HistoryPageSize;
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            ReviewHistoryLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreReviewHistoryAsync()
    {
        try
        {
            int storeId = _appState.CurrentStoreId;
            if (storeId == 0) return;

            ReviewHistoryLoading = true;
            int skip = ReviewHistory.Count;
            JsonElement result = await _apiClient.GetReviewHistoryAsync(storeId, skip, HistoryPageSize + 1);

            int count = 0;
            foreach (JsonElement r in result.EnumerateArray())
            {
                count++;
                if (count > HistoryPageSize) break;
                ReviewHistory.Add(ParseHistoryItem(r));
            }

            HasMoreHistory = count > HistoryPageSize;
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to load more history: {ex.Message}";
        }
        finally
        {
            ReviewHistoryLoading = false;
        }
    }

    [RelayCommand]
    private async Task ClearReviewHistoryAsync()
    {
        if (ReviewHistory.Count == 0) return;

        System.Windows.MessageBoxResult confirm = System.Windows.MessageBox.Show(
            "Are you sure you want to delete all review request history for this store? This cannot be undone.",
            "Clear Review History",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        ReviewErrorMessage = string.Empty;
        ReviewSuccessMessage = string.Empty;

        try
        {
            int storeId = _appState.CurrentStoreId;
            await _apiClient.ClearReviewHistoryAsync(storeId);
            ReviewHistory.Clear();
            HasMoreHistory = false;
            ReviewSuccessMessage = "Review history cleared.";
        }
        catch (Exception ex)
        {
            ReviewErrorMessage = $"Failed to clear history: {ex.Message}";
        }
    }

    private static ReviewHistoryItem ParseHistoryItem(JsonElement r)
    {
        return new ReviewHistoryItem
        {
            ReviewRequestId = r.TryGetProperty("reviewRequestId", out var idE) ? idE.GetInt32() : 0,
            Phone = r.TryGetProperty("phoneE164", out var phE) ? phE.GetString() ?? "" : "",
            PlatformName = r.TryGetProperty("platformName", out var pnE) && pnE.ValueKind == JsonValueKind.String
                ? pnE.GetString() ?? "—"
                : "—",
            MessageBody = r.TryGetProperty("messageBody", out var mbE) ? mbE.GetString() ?? "" : "",
            Status = r.TryGetProperty("status", out var stE) ? stE.GetString() ?? "" : "",
            SentAt = r.TryGetProperty("sentAt", out var saE) && saE.TryGetDateTime(out DateTime dt)
                ? dt.ToLocalTime()
                : DateTime.MinValue
        };
    }

}
