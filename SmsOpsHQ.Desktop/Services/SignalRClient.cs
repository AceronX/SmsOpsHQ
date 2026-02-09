using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace SmsOpsHQ.Desktop.Services;

// Real-time client that connects to the SignalR hub at /hubs/smsops.
// Replaces the Python WebSocket client with auto-reconnect and store groups.
public sealed class SignalRClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly AppState _appState;
    private readonly string _baseUrl;

    // Events raised on the UI thread by callers.
    public event Action<JsonElement, JsonElement>? MessageReceived;
    public event Action<int, int, string, string?>? StatusUpdated;
    public event Action<string, string, string>? SystemAlertReceived;
    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public SignalRClient(string baseUrl, AppState appState)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _appState = appState;
    }

    // Connect to the SignalR hub with JWT authentication and auto-reconnect.
    public async Task ConnectAsync()
    {
        if (_connection is not null)
            await DisposeAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/smsops", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_appState.AccessToken);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _connection.Reconnecting += _ =>
        {
            _appState.IsSignalRConnected = false;
            ConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            _appState.IsSignalRConnected = true;
            ConnectionStateChanged?.Invoke(true);
            await JoinStoreGroupAsync(_appState.CurrentStoreId);
        };

        _connection.Closed += _ =>
        {
            _appState.IsSignalRConnected = false;
            ConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        // Register event handlers for server-to-client messages.
        _connection.On<JsonElement, JsonElement>("MessageNew", (message, thread) =>
        {
            MessageReceived?.Invoke(message, thread);
        });

        _connection.On<int, int, string, string?>("MessageStatus", (storeId, messageId, status, errorCode) =>
        {
            StatusUpdated?.Invoke(storeId, messageId, status, errorCode);
        });

        _connection.On<string, string, string>("SystemAlert", (code, message, severity) =>
        {
            SystemAlertReceived?.Invoke(code, message, severity);
        });

        await _connection.StartAsync();
        _appState.IsSignalRConnected = true;
        ConnectionStateChanged?.Invoke(true);

        // Join the user's store group.
        if (_appState.CurrentStoreId > 0)
            await JoinStoreGroupAsync(_appState.CurrentStoreId);
    }

    // Join a store broadcast group.
    public async Task JoinStoreGroupAsync(int storeId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("JoinStoreGroup", storeId);
    }

    // Leave a store broadcast group.
    public async Task LeaveStoreGroupAsync(int storeId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("LeaveStoreGroup", storeId);
    }

    // Disconnect and dispose the hub connection.
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        _appState.IsSignalRConnected = false;
    }
}
