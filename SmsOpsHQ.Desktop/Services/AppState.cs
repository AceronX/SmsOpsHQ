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

    [ObservableProperty]
    private string _currentStorePhone = string.Empty;

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
        CurrentStoreId = loginResult.User.StoreId ?? 1;
        CurrentStoreName = $"Store {CurrentStoreId}";
        CurrentStorePhone = loginResult.User.StorePhone ?? string.Empty;
        AccessToken = loginResult.AccessToken;
        IsConnected = true;
    }

    // Clear all state on logout.
    public void ClearState()
    {
        CurrentUser = null;
        CurrentStoreId = 0;
        CurrentStoreName = string.Empty;
        CurrentStorePhone = string.Empty;
        AccessToken = string.Empty;
        IsConnected = false;
        IsSignalRConnected = false;
    }

    // Whether the current user has HQ-level access.
    public bool IsHqUser => CurrentUser?.Role is "HQAdmin" or "HQViewer";

    // Updates the current user's display name (e.g. after profile save).
    public void SetCurrentUserFullName(string fullName)
    {
        if (CurrentUser is null) return;
        CurrentUser = new UserDto
        {
            UserId = CurrentUser.UserId,
            Username = CurrentUser.Username,
            FullName = fullName,
            StoreId = CurrentUser.StoreId,
            Role = CurrentUser.Role,
            StorePhone = CurrentUser.StorePhone
        };
    }
}
