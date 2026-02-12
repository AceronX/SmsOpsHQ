using CommunityToolkit.Mvvm.ComponentModel;
using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Desktop.Services;

// Observable application state shared across all ViewModels.
// Holds current user, store, and connection status.
public sealed partial class AppState : ObservableObject
{
    [ObservableProperty]
    private UserDto? _currentUser;

    [ObservableProperty]
    private int _currentStoreId;

    [ObservableProperty]
    private string _currentStoreName = string.Empty;

    // Default Twilio number id for the current store (from DB).
    [ObservableProperty]
    private int? _currentTwilioNumberId;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isSignalRConnected;

    [ObservableProperty]
    private string _accessToken = string.Empty;

    // Set all user/store state from a LoginResult.
    public void SetLoginState(LoginResult loginResult)
    {
        CurrentUser = loginResult.User;
        CurrentStoreId = loginResult.User.StoreId ?? 0; // 0 means no store (HQ user)
        CurrentStoreName = loginResult.User.StoreName ?? string.Empty;
        CurrentTwilioNumberId = loginResult.User.TwilioNumberId;
        AccessToken = loginResult.AccessToken;
        IsConnected = true;
    }

    // Clear all state on logout.
    public void ClearState()
    {
        CurrentUser = null;
        CurrentStoreId = 0;
        CurrentStoreName = string.Empty;
        CurrentTwilioNumberId = null;
        AccessToken = string.Empty;
        IsConnected = false;
        IsSignalRConnected = false;
    }

    // Whether the current user has HQ-level access.
    public bool IsHqUser => CurrentUser?.Role is "HQAdmin" or "HQViewer";

    // Sets the current store with name and optional default Twilio number from DB.
    // Store name must be provided (fetched from store table via API).
    // If defaultNumberId > 0 and user doesn't have a TwilioNumberId set, uses store's default.
    // User's TwilioNumberId takes precedence over store's default.
    // defaultNumberId = 0 means no Twilio number set for the store.
    public void SetCurrentStore(int storeId, string storeName, int defaultNumberId = 0)
    {
        if (string.IsNullOrWhiteSpace(storeName))
            throw new ArgumentException("Store name must be provided (fetch from store table)", nameof(storeName));
            
        CurrentStoreId = storeId;
        CurrentStoreName = storeName;
        
        // Only update TwilioNumberId if user doesn't have one set, and store has a default (> 0)
        // User's TwilioNumberId (set in Settings) takes precedence over store's default
        if (!CurrentTwilioNumberId.HasValue && defaultNumberId > 0)
        {
            CurrentTwilioNumberId = defaultNumberId;
        }
    }

    // Updates the current user's store and Twilio number ID (e.g. after profile save).
    public void UpdateCurrentUser(int? storeId, int? twilioNumberId)
    {
        if (CurrentUser is null) return;
        
        CurrentUser = new UserDto
        {
            UserId = CurrentUser.UserId,
            Username = CurrentUser.Username,
            StoreId = storeId ?? CurrentUser.StoreId,
            StoreName = CurrentUser.StoreName,
            TwilioNumberId = twilioNumberId ?? CurrentUser.TwilioNumberId,
            Role = CurrentUser.Role
        };
        
        // Also update CurrentTwilioNumberId to keep them in sync
        if (twilioNumberId.HasValue)
        {
            CurrentTwilioNumberId = twilioNumberId.Value;
        }
    }
}
