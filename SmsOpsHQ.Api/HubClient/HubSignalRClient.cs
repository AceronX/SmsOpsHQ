using Microsoft.AspNetCore.SignalR.Client;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.HubClient;

/// <summary>
/// Maintains a persistent SignalR connection from this store to the HQ Hub.
///
/// Responsibilities:
/// 1. Connect on startup, auto-reconnect on drop. Authenticates via the
///    X-Store-Key header (the same header the REST heartbeat uses).
/// 2. Push heartbeats every Hub:IntervalSeconds. When the connection is up
///    we send via SignalR (low-latency, no per-call TCP setup); when it's
///    down we fall back to POST /api/heartbeat so HQ still gets data.
/// 3. Handle commands pushed FROM HQ:
///       - RunXpdSyncNow            -> trigger an immediate XPD sync
///       - RequestImmediateHeartbeat-> force a fresh heartbeat right now
///
/// Like HeartbeatPusher, this is best-effort: a connection failure must
/// never affect the actual store API. All exceptions are logged and
/// swallowed; the timer keeps the loop alive.
/// </summary>
public interface IHubSignalRClient
{
    void Start();
    void Stop();
    bool IsConnected { get; }
}

public sealed class HubSignalRClient : IHubSignalRClient, IAsyncDisposable
{
    private readonly IHeartbeatPusher _pusher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HubSignalRClient> _logger;
    private readonly object _lock = new();

    private HubConnection? _connection;
    private Timer? _heartbeatTimer;
    private CancellationTokenSource? _cts;
    private bool _started;

    public HubSignalRClient(
        IHeartbeatPusher pusher,
        IServiceScopeFactory scopeFactory,
        ILogger<HubSignalRClient> logger)
    {
        _pusher = pusher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            if (!_pusher.IsConfigured)
            {
                _logger.LogInformation(
                    "Hub SignalR client disabled (Hub:Enabled=false or missing Url/StoreKey).");
                return;
            }
            _started = true;
            _cts = new CancellationTokenSource();

            // BuildConnection does no I/O; the actual connect happens on the
            // first ConnectIfNeededAsync below (so Start() returns quickly
            // and a Hub outage at boot doesn't block API startup).
            _connection = BuildConnection();
            HookCommandHandlers(_connection);

            _ = Task.Run(() => RunConnectLoopAsync(_cts.Token));

            _heartbeatTimer = new Timer(_ =>
            {
                _ = Task.Run(async () =>
                {
                    try { await SendHeartbeatAsync(_cts!.Token); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Heartbeat tick threw");
                    }
                });
            }, null,
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(_pusher.IntervalSeconds));

            _logger.LogInformation(
                "Hub SignalR client starting: target={Url}, every {Interval}s",
                _pusher.HubUrl, _pusher.IntervalSeconds);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_started) return;
            _started = false;

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_connection is not null)
            {
                // Fire-and-forget DisposeAsync: we don't want to block shutdown
                // on a slow network round-trip.
                _ = _connection.DisposeAsync();
                _connection = null;
            }
            _logger.LogInformation("Hub SignalR client stopped");
        }
    }

    private HubConnection BuildConnection()
    {
        string url = _pusher.HubUrl + HubConstants.AgentHubPath;
        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Auth via header (like the REST heartbeat) AND access_token
                // query string for transports that strip headers (the .NET
                // client uses WebSockets which keeps headers, but the redundancy
                // is free).
                options.Headers[HubConstants.StoreKeyHeader] = _pusher.StoreKey;
                options.AccessTokenProvider = () => Task.FromResult<string?>(_pusher.StoreKey);
            })
            // Built-in retry policy: 0s, 2s, 10s, 30s, then every 30s.
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30)
            })
            .Build();
    }

    private void HookCommandHandlers(HubConnection conn)
    {
        // HQ -> store commands. Each handler runs on the SignalR worker thread;
        // we wrap actual work in Task.Run so a slow handler can't stall the
        // connection's message pump.
        conn.On(HubConstants.AgentMethods.RunXpdSyncNow, () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    IXpdSyncService sync = scope.ServiceProvider.GetRequiredService<IXpdSyncService>();
                    if (!sync.TryMarkSyncStarting())
                    {
                        _logger.LogInformation("HQ requested XPD sync but one is already in progress; skipping");
                        return;
                    }
                    _logger.LogInformation("HQ requested immediate XPD sync");
                    SyncResult result = await sync.FullSyncAsync(null, _cts!.Token);
                    _logger.LogInformation(
                        "HQ-triggered XPD sync finished. Success={Success}, duration={DurationSec}s, error={Error}",
                        result.Success, result.DurationSeconds, result.Error ?? "(none)");
                    // After a sync, fire a fresh heartbeat so HQ sees the result without
                    // waiting for the next normal tick.
                    await SendHeartbeatAsync(_cts!.Token);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "RunXpdSyncNow handler failed"); }
            });
        });

        conn.On(HubConstants.AgentMethods.RequestImmediateHeartbeat, () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("HQ requested immediate heartbeat");
                    await SendHeartbeatAsync(_cts!.Token);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "RequestImmediateHeartbeat handler failed"); }
            });
        });

        conn.Reconnecting += err =>
        {
            _logger.LogWarning("Hub SignalR connection lost; reconnecting... ({Reason})", err?.Message ?? "(no error)");
            return Task.CompletedTask;
        };
        conn.Reconnected += id =>
        {
            _logger.LogInformation("Hub SignalR reconnected (cid={ConnId})", id);
            return Task.CompletedTask;
        };
        conn.Closed += err =>
        {
            // After WithAutomaticReconnect exhausts retries, Closed fires.
            // We restart the connect loop so we keep trying forever (the store
            // PC may be offline overnight; we want to come back when network does).
            _logger.LogWarning("Hub SignalR connection closed ({Reason}); will retry", err?.Message ?? "graceful");
            return Task.CompletedTask;
        };
    }

    private async Task RunConnectLoopAsync(CancellationToken ct)
    {
        // Keeps trying to (re-)establish the connection until Stop() is called.
        // WithAutomaticReconnect handles transient drops; this loop handles the
        // initial connect AND the case where reconnect attempts give up.
        TimeSpan backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            HubConnection? conn = _connection;
            if (conn is null) return;

            if (conn.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await conn.StartAsync(ct);
                    _logger.LogInformation("Hub SignalR connected");
                    backoff = TimeSpan.FromSeconds(5);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Hub SignalR connect failed: {Message}. Retrying in {Backoff}s",
                        ex.Message, backoff.TotalSeconds);
                    try { await Task.Delay(backoff, ct); } catch { return; }
                    backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 1.5));
                    continue;
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch { return; }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        if (!_pusher.IsConfigured) return;

        // Always rebuild the payload so each heartbeat reflects the latest state.
        HeartbeatPayload payload;
        try
        {
            payload = await _pusher.BuildPayloadAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat payload build failed; skipping this tick");
            return;
        }

        if (IsConnected)
        {
            try
            {
                await _connection!.InvokeAsync(HubConstants.AgentMethods.ReceiveHeartbeat, payload, ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat over SignalR failed; falling back to REST");
                // fall through to REST below
            }
        }

        // REST fallback: use the existing pusher to POST. It rebuilds the
        // payload internally but the duplicate work is fine -- this only
        // happens when SignalR is down.
        await _pusher.SendOnceAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
