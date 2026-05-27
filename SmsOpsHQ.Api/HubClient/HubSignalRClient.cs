using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR.Client;
using SmsOpsHQ.Core.DTOs;
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

    /// <summary>
    /// Tear down the current connection (if any), re-read Hub settings from
    /// the on-disk overlay, and re-Start. Used by the
    /// <c>POST /api/hub/reload</c> endpoint so the desktop UI's "Save" button
    /// takes effect immediately without an app restart.
    ///
    /// If the new settings have <c>Enabled=false</c> or are missing
    /// Url/StoreKey, this leaves the client cleanly disconnected.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
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

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Hub SignalR client reload requested");

        // Tear down the current connection first. Stop() handles the "not started"
        // case (e.g. previous Reload disabled us) as a no-op, so this is safe.
        Stop();

        // Brief yield so the fire-and-forget DisposeAsync inside Stop() has a
        // chance to actually close the WebSocket before we open a new one. This
        // is cosmetic only -- correctness doesn't depend on it -- but it keeps
        // the log timeline readable.
        try { await Task.Delay(50, cancellationToken); }
        catch (OperationCanceledException) { return; }

        // Refresh the pusher's cached fields from the on-disk overlay.
        _pusher.Reload();

        // Restart. If the new config has Enabled=false or missing Url/StoreKey,
        // Start() will log "disabled" and leave us cleanly disconnected -- which
        // is exactly what the operator just asked for.
        Start();
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

        // Central Twilio webhook relays from HQ. The Hub already looked up the
        // owning store from the phone number and is targeting our deployment
        // SignalR group, but we re-check the deployment id in the payload as
        // defense in depth: a Hub bug, a misconfigured store key, or a
        // mis-routed group send must NEVER cause one store to ingest another
        // store's customer SMS into its local DB.
        conn.On<TwilioInboundRelayPayload>(HubConstants.AgentMethods.DeliverInboundSms, payload =>
        {
            _ = Task.Run(async () =>
            {
                try { await HandleInboundRelayAsync(payload, _cts!.Token); }
                catch (Exception ex) { _logger.LogWarning(ex, "DeliverInboundSms handler failed"); }
            });
        });

        conn.On<TwilioStatusRelayPayload>(HubConstants.AgentMethods.DeliverMessageStatus, payload =>
        {
            _ = Task.Run(async () =>
            {
                try { await HandleStatusRelayAsync(payload, _cts!.Token); }
                catch (Exception ex) { _logger.LogWarning(ex, "DeliverMessageStatus handler failed"); }
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

    /// <summary>
    /// Processes a Hub-relayed inbound SMS by dispatching to the same
    /// <see cref="IInboundSmsProcessor"/> the local HTTP webhook uses.
    /// Internal so unit tests can call it without spinning up a SignalR server.
    /// </summary>
    internal async Task HandleInboundRelayAsync(TwilioInboundRelayPayload payload, CancellationToken ct)
    {
        if (!IsAddressedToUs(payload, payload?.DeploymentId, payload?.MessageSid, nameof(HubConstants.AgentMethods.DeliverInboundSms)))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IInboundSmsProcessor processor = scope.ServiceProvider.GetRequiredService<IInboundSmsProcessor>();

        InboundSmsRequest request = MapToInboundRequest(payload);
        InboundSmsProcessingResult result = await processor.ProcessAsync(request, ct);

        _logger.LogInformation(
            "Hub-relayed inbound SMS: sid={Sid} kind={Kind} store={StoreId} message={MessageId}",
            payload.MessageSid, result.Kind, result.StoreId, result.MessageId);
    }

    /// <summary>
    /// Processes a Hub-relayed delivery status callback by dispatching to the
    /// shared <see cref="IMessageStatusProcessor"/>.
    /// </summary>
    internal async Task HandleStatusRelayAsync(TwilioStatusRelayPayload payload, CancellationToken ct)
    {
        if (!IsAddressedToUs(payload, payload?.DeploymentId, payload?.MessageSid, nameof(HubConstants.AgentMethods.DeliverMessageStatus)))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IMessageStatusProcessor processor = scope.ServiceProvider.GetRequiredService<IMessageStatusProcessor>();

        MessageStatusUpdate update = MapToStatusUpdate(payload);
        MessageStatusProcessingResult result = await processor.ProcessAsync(update, ct);

        _logger.LogInformation(
            "Hub-relayed status callback: sid={Sid} status={Status} kind={Kind} message={MessageId}",
            payload.MessageSid, payload.MessageStatus, result.Kind, result.MessageId);
    }

    // Defense-in-depth guard for both relay handlers: drop the payload (with
    // a structured warning) unless it is non-null AND its DeploymentId matches
    // ours. The Hub already groups by deployment id, but a Hub bug, a wrongly
    // configured store key, or a mis-routed group send must NEVER cause one
    // store to ingest another store's customer SMS into its local DB.
    //
    // NotNullWhen lets the caller treat the original payload as non-null after
    // the guard returns true, so the handler bodies stay clean of `!`/`payload!`.
    private bool IsAddressedToUs(
        [NotNullWhen(true)] object? payload,
        string? payloadDeploymentId,
        string? messageSid,
        string methodName)
    {
        if (payload is null)
        {
            _logger.LogWarning("{Method} received null payload; dropping.", methodName);
            return false;
        }

        string ourDeploymentId = _pusher.DeploymentId ?? string.Empty;
        if (string.IsNullOrEmpty(ourDeploymentId) ||
            !string.Equals(payloadDeploymentId, ourDeploymentId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Method} addressed to deployment {Other} (we are {Mine}); dropping. sid={Sid}",
                methodName, payloadDeploymentId, ourDeploymentId, messageSid);
            return false;
        }

        return true;
    }

    /// <summary>Wire-format → processor-input mapping for the inbound relay.</summary>
    internal static InboundSmsRequest MapToInboundRequest(TwilioInboundRelayPayload p)
    {
        InboundSmsRequest req = new()
        {
            MessageSid = p.MessageSid ?? string.Empty,
            From = p.From ?? string.Empty,
            To = p.To ?? string.Empty,
            Body = p.Body ?? string.Empty,
            NumMedia = p.NumMedia,
            ReceivedAtUtc = p.ReceivedAtUtc == default ? null : p.ReceivedAtUtc,
        };
        if (p.Media is not null)
        {
            foreach (RelayMediaItem m in p.Media)
            {
                if (string.IsNullOrEmpty(m.Url)) continue;
                req.Media.Add(new InboundMediaItem
                {
                    Index = m.Index,
                    Url = m.Url,
                    ContentType = m.ContentType,
                });
            }
        }
        return req;
    }

    /// <summary>Wire-format → processor-input mapping for the status relay.</summary>
    internal static MessageStatusUpdate MapToStatusUpdate(TwilioStatusRelayPayload p) => new()
    {
        MessageSid = p.MessageSid ?? string.Empty,
        MessageStatus = p.MessageStatus ?? string.Empty,
        ErrorCode = p.ErrorCode,
        ReceivedAtUtc = p.ReceivedAtUtc == default ? null : p.ReceivedAtUtc,
    };

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
