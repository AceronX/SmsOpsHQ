using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Api.HubClient;

// Pushes a heartbeat to HQ every N seconds (default 60). Disabled by default;
// the operator turns it on per-store by setting Hub:Url, Hub:StoreKey, and
// Hub:DeploymentId in this store's appsettings.json (or the persisted overlay
// at %AppData%\SmsOpsHQ\hub_config.json -- M2 will add UI for that).
//
// Mirrors the ReminderScheduler / XpdSyncScheduler pattern: Timer-based,
// idempotent Start(), graceful Stop(), best-effort logging only -- a heartbeat
// failure must NEVER affect the actual store API.
public interface IHeartbeatPusher
{
    void Start();
    void Stop();
    Task<bool> SendOnceAsync(CancellationToken cancellationToken = default);
    HeartbeatPusherStatus GetStatus();

    /// <summary>
    /// Build a fresh HeartbeatPayload describing this store right now.
    /// Exposed so the SignalR client can send the same payload over a
    /// persistent connection without going through the REST stack.
    /// </summary>
    Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Hub URL (with trailing / stripped) or empty if not configured.</summary>
    string HubUrl { get; }
    /// <summary>X-Store-Key configured for this deployment, or empty if not configured.</summary>
    string StoreKey { get; }
    /// <summary>Configured deployment id or empty.</summary>
    string DeploymentId { get; }
    /// <summary>Heartbeat cadence in seconds.</summary>
    int IntervalSeconds { get; }
    /// <summary>True when Hub:Enabled=true and the URL/key are present.</summary>
    bool IsConfigured { get; }
}

public sealed class HeartbeatPusherStatus
{
    public bool Enabled { get; set; }
    public bool Running { get; set; }
    public string HubUrl { get; set; } = string.Empty;
    public string DeploymentId { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public string? LastError { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

public sealed class HeartbeatPusher : IHeartbeatPusher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatPusher> _logger;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly object _lock = new();

    private readonly bool _enabled;
    private readonly string _hubUrl;
    private readonly string _storeKey;
    private readonly string _deploymentId;
    private readonly int _intervalSeconds;

    private Timer? _timer;
    private bool _running;
    private DateTime? _lastSuccessUtc;
    private DateTime? _lastAttemptUtc;
    private string? _lastError;
    private int _successCount;
    private int _failureCount;

    public HeartbeatPusher(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatPusher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;

        IConfigurationSection hub = configuration.GetSection("Hub");
        _enabled = hub.GetValue("Enabled", false);
        _hubUrl = (hub["Url"] ?? string.Empty).TrimEnd('/');
        _storeKey = hub["StoreKey"] ?? string.Empty;
        _deploymentId = hub["DeploymentId"] ?? string.Empty;
        _intervalSeconds = hub.GetValue("IntervalSeconds", 60);
        if (_intervalSeconds < 10) _intervalSeconds = 10; // sanity floor
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;

            if (!_enabled)
            {
                _logger.LogInformation(
                    "Hub heartbeat disabled (Hub:Enabled=false). " +
                    "To register this store with HQ, set Hub:Enabled=true plus Hub:Url and Hub:StoreKey.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_hubUrl) || string.IsNullOrWhiteSpace(_storeKey))
            {
                _logger.LogWarning(
                    "Hub heartbeat enabled but Hub:Url or Hub:StoreKey is missing. Skipping.");
                return;
            }

            // First fire after 5s so the API has a chance to finish booting before
            // we hold a DB connection for the metric query.
            _timer = new Timer(OnTimerFired, null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(_intervalSeconds));
            _running = true;

            _logger.LogInformation(
                "Hub heartbeat started: target={Url}, every {Interval}s, deploymentId={Deployment}",
                _hubUrl, _intervalSeconds,
                string.IsNullOrEmpty(_deploymentId) ? "(unset)" : _deploymentId);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _timer?.Dispose();
            _timer = null;
            _running = false;
            _logger.LogInformation("Hub heartbeat stopped");
        }
    }

    private void OnTimerFired(object? state)
    {
        _ = Task.Run(async () =>
        {
            try { await SendOnceAsync(); }
            catch (Exception ex)
            {
                // SendOnceAsync already catches & records; this is a defensive net.
                _logger.LogWarning(ex, "Unhandled error in heartbeat tick");
            }
        });
    }

    public async Task<bool> SendOnceAsync(CancellationToken cancellationToken = default)
    {
        _lastAttemptUtc = DateTime.UtcNow;
        try
        {
            HeartbeatPayload payload = await BuildPayloadAsync(cancellationToken);

            using HttpClient http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Add(HubConstants.StoreKeyHeader, _storeKey);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string url = _hubUrl + "/api/heartbeat";
            using HttpResponseMessage response = await http.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await SafeReadAsync(response, cancellationToken);
                _lastError = $"HTTP {(int)response.StatusCode}: {body}";
                _failureCount++;
                _logger.LogWarning(
                    "Hub heartbeat failed: {Status} {Body}",
                    response.StatusCode, body);
                return false;
            }

            _lastSuccessUtc = DateTime.UtcNow;
            _lastError = null;
            _successCount++;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _failureCount++;
            _logger.LogWarning(ex, "Hub heartbeat threw");
            return false;
        }
    }

    public HeartbeatPusherStatus GetStatus()
    {
        return new HeartbeatPusherStatus
        {
            Enabled = _enabled,
            Running = _running,
            HubUrl = _hubUrl,
            DeploymentId = _deploymentId,
            IntervalSeconds = _intervalSeconds,
            LastSuccessUtc = _lastSuccessUtc,
            LastAttemptUtc = _lastAttemptUtc,
            LastError = _lastError,
            SuccessCount = _successCount,
            FailureCount = _failureCount
        };
    }

    public string HubUrl => _hubUrl;
    public string StoreKey => _storeKey;
    public string DeploymentId => _deploymentId;
    public int IntervalSeconds => _intervalSeconds;
    public bool IsConfigured => _enabled
                                && !string.IsNullOrWhiteSpace(_hubUrl)
                                && !string.IsNullOrWhiteSpace(_storeKey);

    public async Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ITwilioService twilio = scope.ServiceProvider.GetRequiredService<ITwilioService>();
        IXpdSyncService xpdSync = scope.ServiceProvider.GetRequiredService<IXpdSyncService>();
        IXpdSyncScheduler xpdScheduler = scope.ServiceProvider.GetRequiredService<IXpdSyncScheduler>();

        DateTime utcNow = DateTime.UtcNow;
        DateTime localStartOfDay = DateTime.Now.Date;
        DateTime localEndOfDay = localStartOfDay.AddDays(1);

        // Counts are computed in store-local time so "today" matches what the
        // cashier sees in the desktop app, not UTC midnight.
        int sentToday = await db.Messages
            .Where(m => m.Direction == "Outbound"
                        && m.CreatedAt >= localStartOfDay
                        && m.CreatedAt < localEndOfDay)
            .CountAsync(cancellationToken);

        int receivedToday = await db.Messages
            .Where(m => m.Direction == "Inbound"
                        && m.CreatedAt >= localStartOfDay
                        && m.CreatedAt < localEndOfDay)
            .CountAsync(cancellationToken);

        int unreadCount = await db.Threads.SumAsync(t => (int)t.UnreadCount, cancellationToken);

        int customerCount = await db.Customers.CountAsync(cancellationToken);
        int activeTicketCount = await db.Tickets.CountAsync(t => t.Active == 1, cancellationToken);

        DateTime? lastUserActivity = await db.Users
            .Where(u => u.LastLoginAt != null)
            .OrderByDescending(u => u.LastLoginAt)
            .Select(u => u.LastLoginAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Store name: best-effort. If multiple stores in this DB, use the first.
        string storeName = await db.Stores
            .OrderBy(s => s.StoreId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        SyncStatus syncStatus = xpdSync.GetSyncStatus();
        XpdSyncSchedulerStatus schedulerStatus = xpdScheduler.GetStatus();
        DateTime? schedulerNextUtc = ParseLocalDateTime(schedulerStatus.NextRunTime);

        return new HeartbeatPayload
        {
            DeploymentId = _deploymentId,
            StoreName = storeName,
            AppVersion = GetAppVersion(),
            SentAtUtc = utcNow,
            Uptime = utcNow - _startedUtc,

            TwilioMode = twilio.IsMockMode ? "mock" : "live",
            TwilioMock = twilio.IsMockMode,

            XpdLastSyncUtc = syncStatus.LastSync,
            XpdLastSyncSuccess = string.IsNullOrEmpty(syncStatus.Error) && syncStatus.LastSync is not null,
            XpdLastSyncError = syncStatus.Error,
            XpdSchedulerRunning = schedulerStatus.Running,
            XpdSchedulerNextRunUtc = schedulerNextUtc,

            MessagesSentToday = sentToday,
            MessagesReceivedToday = receivedToday,
            UnreadCount = unreadCount,
            CustomerCount = customerCount,
            ActiveTicketCount = activeTicketCount,

            LastUserActivityUtc = lastUserActivity,
            OnlineUserCount = 0  // M3: track via SignalR connection count
        };
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly();
        if (asm is null) return "unknown";
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (info is not null && !string.IsNullOrEmpty(info.InformationalVersion))
            return info.InformationalVersion.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "unknown";
    }

    // Scheduler stores NextRunTime as "yyyy-MM-dd HH:mm:ss" (local). Convert
    // to UTC so the dashboard renders consistent times across stores in
    // different timezones (M2 will add per-store TZ to fix the conversion).
    private static DateTime? ParseLocalDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, out DateTime local))
            return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return null;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return (await resp.Content.ReadAsStringAsync(ct)).Trim(); }
        catch { return ""; }
    }

    public void Dispose() => Stop();
}
