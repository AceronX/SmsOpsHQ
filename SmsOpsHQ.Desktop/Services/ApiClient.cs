using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Desktop.Services;

// Central HTTP client for all API communication.
// All 60+ endpoints from the Python api_client.py are covered here.
public sealed class ApiClient : ICustomerPanelApi, IDisposable
{
    private readonly HttpClient _httpClient;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;
    public HttpClient Http => _httpClient;

    public void SetAuthToken(string accessToken) =>
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    public void ClearAuthToken() =>
        _httpClient.DefaultRequestHeaders.Authorization = null;

    // ── Auth ─────────────────────────────────────────────────────────

    public async Task<LoginResult?> LoginAsync(string username, string password)
    {
        LoginRequest request = new() { Username = username, Password = password };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/auth/login", request, JsonOptions);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions);
    }

    public async Task UpdateProfileAsync(string? username = null, int? storeId = null, int? twilioNumberId = null)
    {
        var request = new { username, storeId, twilioNumberId };
        await PutJsonAsync("/api/auth/profile", request);
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var request = new { oldPassword, newPassword };
        await PostJsonAsync("/api/auth/change-password", request);
    }

    // ── Messages ─────────────────────────────────────────────────────

    public async Task<JsonElement> SendMessageAsync(SendMessageRequest request) =>
        await PostJsonAsync("/api/send", request);

    public async Task<JsonElement> CreateNoteAsync(int threadId, CreateNoteRequest request) =>
        await PostJsonAsync($"/api/thread/{threadId}/notes", request);

    public async Task<JsonElement> GetMessagesAsync(int storeId, string? category = null, int? threadId = null, int limit = 100, int offset = 0)
    {
        string url = $"/api/messages?store_id={storeId}&limit={limit}&offset={offset}";
        if (!string.IsNullOrEmpty(category)) url += $"&category={category}";
        if (threadId.HasValue) url += $"&thread_id={threadId.Value}";
        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> GetMessageCountsAsync(int storeId, int? threadId = null)
    {
        string url = $"/api/messages/counts?store_id={storeId}";
        if (threadId.HasValue) url += $"&thread_id={threadId.Value}";
        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> GetCategoriesAsync() =>
        await GetJsonAsync("/api/messages/categories");

    // ── Threads / Inbox ──────────────────────────────────────────────

    public async Task<JsonElement> GetInboxAsync(int storeId, string filter = "open", string? search = null, int? twilioNumberId = null)
    {
        string url = $"/api/inbox?store_id={storeId}&filter={filter}";
        if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (twilioNumberId.HasValue) url += $"&twilio_number_id={twilioNumberId.Value}";
        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> GetThreadAsync(int storeId, int threadId, bool includeXpd = false)
    {
        string url = $"/api/thread/{threadId}?store_id={storeId}";
        if (includeXpd) url += "&include_xpd=true";
        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> DeleteThreadAsync(int storeId, int threadId) =>
        await DeleteJsonAsync($"/api/thread/{threadId}?store_id={storeId}");

    public async Task<JsonElement> DeleteAllConversationsAsync(int storeId) =>
        await DeleteJsonAsync($"/api/conversations?store_id={storeId}");

    public async Task<JsonElement> GetThreadsBulkAsync(int storeId, List<int> threadIds)
    {
        string ids = string.Join(",", threadIds);
        return await GetJsonAsync($"/api/threads/bulk?store_id={storeId}&thread_ids={ids}");
    }

    public async Task<JsonElement> GetMessagesBulkAsync(int storeId) =>
        await GetJsonAsync($"/api/messages/bulk?store_id={storeId}");

    // ── Customers ────────────────────────────────────────────────────

    public async Task<JsonElement> SearchCustomersAsync(string query, int limit = 10) =>
        await GetJsonAsync($"/api/customers/search?q={Uri.EscapeDataString(query)}&limit={limit}");

    public async Task<JsonElement> GetCustomerContextAsync(int customerId) =>
        await GetJsonAsync($"/api/customer/{customerId}/context");

    public async Task<JsonElement> UpdateCustomerAsync(int customerId, UpdateCustomerRequest request) =>
        await PostJsonAsync($"/api/customer/{customerId}/update", request);

    public async Task<JsonElement> GetLateCustomersAsync(
        string? query = null,
        bool includePulled = false)
    {
        var request = new { Query = query, IncludePulled = includePulled };
        return await PostJsonAsync("/api/customers/late", request);
    }

    public async Task<JsonElement> MoveLateTicketToPullListAsync(
        int storeId,
        int ticketKey,
        int customerKey,
        string? reason = null)
    {
        var request = new { storeId, ticketKey, customerKey, reason };
        return await PostJsonAsync("/api/late-customers/pull-list", request);
    }

    public async Task RestoreLateTicketFromPullListAsync(int storeId, int ticketKey) =>
        _ = await DeleteJsonAsync(
            $"/api/late-customers/pull-list/{ticketKey}?storeId={storeId}");

    public async Task<JsonElement> GetPfxCustomersAsync(int days = 60) =>
        await GetJsonAsync($"/api/customers/pfx?days={days}");

    public async Task<JsonElement> GetCustomerByPhoneAsync(
        string phone,
        int? selectedCustomerKey = null,
        CancellationToken cancellationToken = default)
    {
        string url = $"/api/customer/by-phone?phone={Uri.EscapeDataString(phone)}";
        if (selectedCustomerKey is int k)
            url += $"&selectedCustomerKey={k}";
        return await GetJsonAsync(url, cancellationToken);
    }

    public async Task<byte[]?> GetCustomerIdPhotoBytesAsync(
        int customerKey,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"/api/customer/id-photo?customerKey={customerKey}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Obsolete("Compatibility only. Use CreateCustomerAppNoteAsync.")]
    public async Task<JsonElement> AppendNoteXpdAsync(int customerKey, string note)
    {
        var request = new { customerKey, note };
        return await PostJsonAsync("/api/customers/append-note-xpd", request);
    }

    public async Task<JsonElement> GetCustomerQualityAsync(
        int customerKey,
        string qualityMetric = "default",
        CancellationToken cancellationToken = default)
    {
        var request = new { customerKey, qualityMetric };
        return await PostJsonAsync("/api/customers/quality", request, cancellationToken);
    }

    public async Task<JsonElement> GetCustomerAppNotesAsync(
        int customerKey,
        CancellationToken cancellationToken = default) =>
        await GetJsonAsync($"/api/customers/{customerKey}/app-notes", cancellationToken);

    public async Task<JsonElement> CreateCustomerAppNoteAsync(
        int customerKey,
        string content,
        CancellationToken cancellationToken = default)
    {
        var request = new { content };
        return await PostJsonAsync(
            $"/api/customers/{customerKey}/app-notes",
            request,
            cancellationToken);
    }

    public async Task<JsonElement> TestSqliteAsync(string? path = null)
    {
        string url = string.IsNullOrWhiteSpace(path)
            ? "/api/test-sqlite"
            : "/api/test-sqlite?path=" + Uri.EscapeDataString(path.Trim());
        return await GetJsonAsync(url);
    }

    // ── Templates ────────────────────────────────────────────────────

    public async Task<JsonElement> GetTemplatesAsync(int storeId, string? category = null)
    {
        string url = $"/api/templates?store_id={storeId}";
        if (!string.IsNullOrEmpty(category)) url += $"&category={Uri.EscapeDataString(category)}";
        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> CreateTemplateAsync(TemplateCreateRequest request) =>
        await PostJsonAsync("/api/templates", request);

    public async Task<JsonElement> UpdateTemplateAsync(int templateId, TemplateCreateRequest request) =>
        await PutJsonAsync($"/api/templates/{templateId}", request);

    public async Task<JsonElement> DeleteTemplateAsync(int templateId) =>
        await DeleteJsonAsync($"/api/templates/{templateId}");

    // ── Reviews ──────────────────────────────────────────────────────

    public async Task<JsonElement> SendReviewRequestAsync(int storeId, string phone, int? twilioNumberId)
    {
        var request = new { storeId, customerPhone = phone, twilioNumberId };
        return await PostJsonAsync("/api/reviews/send", request);
    }

    public async Task<ReviewReadinessDto> GetReviewReadinessAsync(int storeId, int? twilioNumberId)
    {
        string url = $"/api/reviews/readiness?storeId={storeId}";
        if (twilioNumberId.HasValue)
            url += $"&twilioNumberId={twilioNumberId.Value}";

        JsonElement json = await GetJsonAsync(url);
        return JsonSerializer.Deserialize<ReviewReadinessDto>(json.GetRawText(), JsonOptions)
               ?? new ReviewReadinessDto();
    }

    public async Task<JsonElement> GetReviewHistoryAsync(int storeId, int skip = 0, int take = 50) =>
        await GetJsonAsync($"/api/reviews/history?storeId={storeId}&skip={skip}&take={take}");

    public async Task<JsonElement> ClearReviewHistoryAsync(int storeId) =>
        await DeleteJsonAsync($"/api/reviews/history?storeId={storeId}");

    public async Task<JsonElement> GetReviewChannelsAsync(int storeId) =>
        await GetJsonAsync($"/api/reviews/channels?storeId={storeId}");

    public async Task<JsonElement> CreateReviewChannelAsync(int storeId, string platformName, string reviewUrl, int sortOrder)
    {
        var request = new { storeId, platformName, reviewUrl, sortOrder };
        return await PostJsonAsync("/api/reviews/channels", request);
    }

    public async Task<JsonElement> UpdateReviewChannelAsync(int channelId, string platformName, string reviewUrl, int sortOrder, bool isActive)
    {
        var request = new { platformName, reviewUrl, sortOrder, isActive };
        return await PutJsonAsync($"/api/reviews/channels/{channelId}", request);
    }

    public async Task<JsonElement> DeleteReviewChannelAsync(int channelId) =>
        await DeleteJsonAsync($"/api/reviews/channels/{channelId}");

    // ── Review automation (new XPD tickets → review SMS) ────────────

    public async Task<JsonElement> GetReviewAutomationSettingsAsync() =>
        await GetJsonAsync("/api/review-automation/settings");

    public async Task<JsonElement> PutReviewAutomationSettingsAsync(bool enabled, int intervalMinutes, bool runOnStartup)
    {
        var body = new { enabled, intervalMinutes, runOnStartup };
        return await PutJsonAsync("/api/review-automation/settings", body);
    }

    public async Task<JsonElement> GetReviewAutomationStatusAsync() =>
        await GetJsonAsync("/api/review-automation/status");

    public async Task<JsonElement> RunReviewAutomationNowAsync() =>
        await PostJsonAsync("/api/review-automation/run", null);

    // ── Stores ───────────────────────────────────────────────────────

    public async Task<JsonElement> GetStoresAsync() =>
        await GetJsonAsync("/api/stores");

    public async Task<JsonElement> GetStoreAsync(int storeId) =>
        await GetJsonAsync($"/api/stores/{storeId}");

    public async Task<JsonElement> CreateStoreAsync(string storeName)
    {
        var request = new { storeName = storeName.Trim() };
        return await PostJsonAsync("/api/stores", request);
    }

    public async Task<JsonElement> GetTwilioNumbersAsync(int storeId) =>
        await GetJsonAsync($"/api/stores/{storeId}/numbers");

    public async Task<JsonElement> AddNumberAsync(int storeId, string phone)
    {
        var request = new { phone };
        return await PostJsonAsync($"/api/stores/{storeId}/numbers", request);
    }

    public async Task<JsonElement> SetDefaultNumberAsync(int storeId, int numberId)
    {
        var request = new { numberId };
        return await PostJsonAsync($"/api/stores/{storeId}/default-number", request);
    }

    public async Task<JsonElement> UpdateNumberAsync(int storeId, int numberId, string phone)
    {
        var request = new { phone };
        return await PutJsonAsync($"/api/stores/{storeId}/numbers/{numberId}", request);
    }

    public async Task<JsonElement> DeleteNumberAsync(int storeId, int numberId) =>
        await DeleteJsonAsync($"/api/stores/{storeId}/numbers/{numberId}");

    // ── Reminders ────────────────────────────────────────────────────

    public async Task<JsonElement> SendReminderAsync(int ticketKey, int customerKey, string phone, string transNo, string dueDate, int daysDiff)
    {
        var request = new { ticketKey, customerKey, phone, transNo, dueDate, daysDiff };
        return await PostJsonAsync("/api/reminders/send", request);
    }

    public async Task<JsonElement> RunBatchRemindersAsync(int maxCount = 100)
    {
        var request = new { maxCount };
        return await PostJsonAsync("/api/reminders/batch", request);
    }

    public async Task<JsonElement> RunAutoRemindersAsync() =>
        await PostJsonAsync("/api/reminders/auto", null);

    public async Task<SchedulerStatus?> GetSchedulerStatusAsync() =>
        await _httpClient.GetFromJsonAsync<SchedulerStatus>("/api/reminders/scheduler/status", JsonOptions);

    public async Task StartSchedulerAsync() =>
        (await _httpClient.PostAsync("/api/reminders/scheduler/start", null)).EnsureSuccessStatusCode();

    public async Task StopSchedulerAsync() =>
        (await _httpClient.PostAsync("/api/reminders/scheduler/stop", null)).EnsureSuccessStatusCode();

    public async Task<JsonElement> GetReminderHistoryByTicketAsync(int ticketKey) =>
        await GetJsonAsync($"/api/reminders/history/ticket/{ticketKey}");

    public async Task<JsonElement> GetReminderHistoryByCustomerAsync(int customerKey) =>
        await GetJsonAsync($"/api/reminders/history/customer/{customerKey}");

    public async Task<JsonElement> GetReminderHistoryByPhoneAsync(string phone) =>
        await GetJsonAsync($"/api/reminders/history/phone/{Uri.EscapeDataString(phone)}");

    public async Task<JsonElement> GetNextReminderAsync(int ticketKey, string dueDate, int daysLate) =>
        await GetJsonAsync($"/api/reminders/next/{ticketKey}?dueDate={Uri.EscapeDataString(dueDate)}&daysLate={daysLate}");

    public async Task<JsonElement> GetReminderStatisticsAsync() =>
        await GetJsonAsync("/api/reminders/statistics");

    public async Task<JsonElement> GetRecentRemindersAsync(int limit = 20) =>
        await GetJsonAsync($"/api/reminders/recent?limit={limit}");

    public async Task<JsonElement> GetSentRemindersAsync(int limit = 100) =>
        await GetJsonAsync($"/api/reminders/sent?limit={limit}");

    public async Task<JsonElement> ExcludePhoneAsync(string phone, string? reason = null)
    {
        var request = new { phone, reason };
        return await PostJsonAsync("/api/reminders/exclude", request);
    }

    public async Task<JsonElement> UnsubscribePhoneAsync(string phone, string method = "MANUAL", string? notes = null)
    {
        var request = new { phone, method, notes };
        return await PostJsonAsync("/api/reminders/unsubscribe", request);
    }

    public async Task<JsonElement> CheckExcludedAsync(string phone) =>
        await GetJsonAsync($"/api/reminders/excluded/{Uri.EscapeDataString(phone)}");

    // ── Sync ─────────────────────────────────────────────────────────

    public async Task<JsonElement> GetSyncConfigAsync() =>
        await GetJsonAsync("/api/sync/config");

    public async Task<JsonElement> GetSyncStatusAsync() =>
        await GetJsonAsync("/api/sync/status");

    public async Task<JsonElement> GetSyncProgressAsync() =>
        await GetJsonAsync("/api/sync/progress");

    public async Task<JsonElement> TriggerSyncAsync(string? xpdPath = null, string? mdwPath = null, string? xpdUser = null, string? xpdPassword = null)
    {
        var options = new SyncRunOptions
        {
            XpdPath = xpdPath,
            MdwPath = mdwPath,
            XpdUser = xpdUser,
            XpdPassword = xpdPassword
        };
        return await PostJsonAsync("/api/sync/full", options);
    }

    /// <summary>
    /// Persists XPD path/credentials on the API so BOTH manual sync and the hourly
    /// auto-sync use them. Empty fields are kept at their previously-saved value.
    /// </summary>
    public async Task<JsonElement> SaveSyncConfigAsync(string? xpdPath, string? mdwPath, string? xpdUser, string? xpdPassword)
    {
        var options = new SyncRunOptions
        {
            XpdPath = xpdPath,
            MdwPath = mdwPath,
            XpdUser = xpdUser,
            XpdPassword = xpdPassword
        };
        return await PostJsonAsync("/api/sync/config", options);
    }

    /// <summary>
    /// Returns a one-shot health snapshot for the XPD sync prerequisites
    /// (file exists, MDW present, store created, scheduler state, etc.)
    /// so the UI can show a clear go/no-go before clicking Run sync now.
    /// </summary>
    public async Task<JsonElement> GetSyncPreflightAsync() =>
        await GetJsonAsync("/api/sync/preflight");

    public async Task<JsonElement> GetSyncCountsAsync() =>
        await GetJsonAsync("/api/sync/counts");

    // ── Quarantine ───────────────────────────────────────────────────

    public async Task<JsonElement> GetQuarantinedAsync(int limit = 50) =>
        await GetJsonAsync($"/api/quarantine/list?limit={limit}");

    public async Task<JsonElement> ResolveQuarantineAsync(int quarantineId, string action)
    {
        var request = new { action };
        return await PostJsonAsync($"/api/quarantine/{quarantineId}/resolve", request);
    }

    public async Task<JsonElement> GetQuarantineStatsAsync() =>
        await GetJsonAsync("/api/quarantine/stats");

    // ── Media ────────────────────────────────────────────────────────

    public async Task<byte[]> ProxyMediaAsync(string url)
    {
        HttpResponseMessage response = await _httpClient.GetAsync($"/api/media/proxy?url={Uri.EscapeDataString(url)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // ── Metrics ──────────────────────────────────────────────────────

    public async Task<JsonElement> GetThreadLoadingMetricsAsync() =>
        await GetJsonAsync("/api/metrics/thread-loading");

    public async Task<JsonElement> GetXpdLimiterMetricsAsync() =>
        await GetJsonAsync("/api/metrics/xpd-limiter");

    // ── Health ───────────────────────────────────────────────────────

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Twilio diagnostics ───────────────────────────────────────────

    /// <summary>Reads /api/twilio/status. Returns null if the API is unreachable.</summary>
    public async Task<TwilioStatusInfo?> GetTwilioStatusAsync()
    {
        try
        {
            JsonElement json = await GetJsonAsync("/api/twilio/status");
            return new TwilioStatusInfo
            {
                Mock = json.TryGetProperty("mock", out JsonElement mE) && mE.GetBoolean(),
                Mode = json.TryGetProperty("mode", out JsonElement modeE) ? modeE.GetString() ?? "" : "",
                AccountSidPrefix = json.TryGetProperty("account_sid_prefix", out JsonElement sidE)
                    ? sidE.GetString() ?? ""
                    : "",
                HasMessagingService = json.TryGetProperty("has_messaging_service", out JsonElement hmE)
                    && hmE.GetBoolean(),
                Warning = json.TryGetProperty("warning", out JsonElement wE) && wE.ValueKind == JsonValueKind.String
                    ? wE.GetString()
                    : null
            };
        }
        catch
        {
            return null;
        }
    }

    // ── HQ Hub ───────────────────────────────────────────────────────

    public async Task<HubStatusResult> GetHubStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("/api/hub/status", cancellationToken);
        JsonElement result = await ProcessResponseAsync(response);
        return ParseHubStatus(result);
    }

    /// <summary>
    /// Tells the local API to re-read hub_config.json and rebuild its SignalR
    /// connection in-place. Used by Settings &gt; HQ Hub &gt; Save so the operator
    /// does not have to restart the app for new Hub credentials to take effect.
    /// </summary>
    public async Task<HubReloadResult> ReloadHubAsync()
    {
        JsonElement result = await PostJsonAsync("/api/hub/reload");
        HubStatusResult status = ParseHubStatus(result);
        return new HubReloadResult
        {
            Enabled = status.Enabled,
            Configured = status.Configured,
            IsConnected = status.IsConnected,
            HubUrl = status.HubUrl,
            DeploymentId = status.DeploymentId,
            IntervalSeconds = status.IntervalSeconds
        };
    }

    private static HubStatusResult ParseHubStatus(JsonElement result)
    {
        return new HubStatusResult
        {
            Enabled = result.TryGetProperty("enabled", out JsonElement en) && en.GetBoolean(),
            Configured = result.TryGetProperty("configured", out JsonElement co) && co.GetBoolean(),
            IsConnected = result.TryGetProperty("isConnected", out JsonElement ic) && ic.GetBoolean(),
            HubUrl = result.TryGetProperty("hubUrl", out JsonElement u) ? u.GetString() ?? string.Empty : string.Empty,
            DeploymentId = result.TryGetProperty("deploymentId", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty,
            IntervalSeconds = result.TryGetProperty("intervalSeconds", out JsonElement i) && i.TryGetInt32(out int s) ? s : 0,
            LastAttemptUtc = ReadNullableDateTime(result, "lastAttemptUtc"),
            LastSuccessUtc = ReadNullableDateTime(result, "lastSuccessUtc"),
            LastError = result.TryGetProperty("lastError", out JsonElement e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null,
            SuccessCount = result.TryGetProperty("successCount", out JsonElement sc) && sc.TryGetInt32(out int successes)
                ? successes
                : 0,
            FailureCount = result.TryGetProperty("failureCount", out JsonElement fc) && fc.TryGetInt32(out int failures)
                ? failures
                : 0
        };
    }

    private static DateTime? ReadNullableDateTime(JsonElement result, string propertyName)
    {
        return result.TryGetProperty(propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
               && value.TryGetDateTime(out DateTime parsed)
            ? parsed
            : null;
    }

    public sealed class HubReloadResult
    {
        public bool Enabled { get; init; }
        public bool Configured { get; init; }
        public bool IsConnected { get; init; }
        public string HubUrl { get; init; } = string.Empty;
        public string DeploymentId { get; init; } = string.Empty;
        public int IntervalSeconds { get; init; }
    }

    public sealed class HubStatusResult
    {
        public bool Enabled { get; init; }
        public bool Configured { get; init; }
        public bool IsConnected { get; init; }
        public string HubUrl { get; init; } = string.Empty;
        public string DeploymentId { get; init; } = string.Empty;
        public int IntervalSeconds { get; init; }
        public DateTime? LastAttemptUtc { get; init; }
        public DateTime? LastSuccessUtc { get; init; }
        public string? LastError { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
    }

    // ── XPD hourly auto-sync scheduler ───────────────────────────────────

    /// <summary>
    /// Returns the live status of the hourly XPD auto-sync scheduler -- whether
    /// it's running, the next/last run times, success/failure counts. Used by
    /// Settings -&gt; XPD to render the status line under the toggle.
    /// </summary>
    public async Task<XpdSchedulerStatus> GetSyncSchedulerStatusAsync()
    {
        JsonElement r = await GetJsonAsync("/api/sync/scheduler/status");
        return ParseSchedulerStatus(r);
    }

    /// <summary>
    /// Tells the API to re-read <c>xpd_sync_config.json</c> and restart the
    /// scheduler in place. Used by Settings -&gt; XPD -&gt; "Save auto-sync"
    /// right after <c>XpdSyncSchedulerConfigService.Save</c> writes the file,
    /// so the operator does NOT have to restart the app for the new Enabled /
    /// IntervalMinutes / RunOnStartup to take effect.
    /// </summary>
    public async Task<XpdSchedulerStatus> ReloadSyncSchedulerAsync()
    {
        JsonElement r = await PostJsonAsync("/api/sync/scheduler/reload");
        return ParseSchedulerStatus(r);
    }

    private static XpdSchedulerStatus ParseSchedulerStatus(JsonElement r)
    {
        return new XpdSchedulerStatus
        {
            Running = r.TryGetProperty("running", out JsonElement run) && run.GetBoolean(),
            IntervalMinutes = r.TryGetProperty("intervalMinutes", out JsonElement im)
                              && im.TryGetInt32(out int imv) ? imv : 0,
            NextRunTime = r.TryGetProperty("nextRunTime", out JsonElement n)
                          && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
            LastRunTime = r.TryGetProperty("lastRunTime", out JsonElement l)
                          && l.ValueKind == JsonValueKind.String ? l.GetString() : null,
            LastRunSuccess = r.TryGetProperty("lastRunSuccess", out JsonElement ls)
                             && ls.GetBoolean(),
            LastRunError = r.TryGetProperty("lastRunError", out JsonElement le)
                           && le.ValueKind == JsonValueKind.String ? le.GetString() : null,
            TotalRunCount = r.TryGetProperty("totalRunCount", out JsonElement tc)
                            && tc.TryGetInt32(out int tcv) ? tcv : 0,
            SuccessCount = r.TryGetProperty("successCount", out JsonElement sc)
                           && sc.TryGetInt32(out int scv) ? scv : 0,
            FailureCount = r.TryGetProperty("failureCount", out JsonElement fc)
                           && fc.TryGetInt32(out int fcv) ? fcv : 0,
            RunInProgress = r.TryGetProperty("runInProgress", out JsonElement rip)
                            && rip.GetBoolean()
        };
    }

    /// <summary>Decoded result of <c>GET /api/sync/scheduler/status</c> and reload.</summary>
    public sealed class XpdSchedulerStatus
    {
        public bool Running { get; init; }
        public int IntervalMinutes { get; init; }
        public string? NextRunTime { get; init; }
        public string? LastRunTime { get; init; }
        public bool LastRunSuccess { get; init; }
        public string? LastRunError { get; init; }
        public int TotalRunCount { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
        public bool RunInProgress { get; init; }
    }

    /// <summary>
    /// Asks the local API to gracefully close its Hub SignalR connection
    /// (sending the SignalR "goodbye" frame so the Hub fires
    /// OnDisconnectedAsync immediately rather than waiting for the keepalive
    /// timeout). Called from <c>App.OnExit</c> right before the bundled API
    /// process is killed.
    ///
    /// Bounded so the shutdown path never hangs: passes a 3-second
    /// HttpClient timeout AND treats <em>any</em> failure as a successful
    /// no-op. The worst case (API already dead, network gone, 401 because the
    /// user is logged out) is that the Hub falls back to its keepalive
    /// timeout (~15s with the tightened defaults) -- annoying but not broken.
    /// </summary>
    public async Task ShutdownHubAsync()
    {
        using HttpRequestMessage req = new(HttpMethod.Post, "/api/hub/shutdown")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        // Local HttpClient with its own short timeout so the BaseAddress'd
        // long-lived client isn't disturbed and the WPF shutdown path can't
        // hang on a slow/dead localhost API.
        using HttpClient bounded = new()
        {
            BaseAddress = _httpClient.BaseAddress,
            Timeout = TimeSpan.FromSeconds(3)
        };
        bounded.DefaultRequestHeaders.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
        try
        {
            using HttpResponseMessage _ = await bounded.SendAsync(req).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: shutdown must never throw on the way out.
        }
    }

    // ── HTTP helpers ─────────────────────────────────────────────────

    private async Task<JsonElement> GetJsonAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        return await ProcessResponseAsync(response);
    }

    private async Task<JsonElement> PostJsonAsync(
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = body is null
            ? await _httpClient.PostAsync(url, null, cancellationToken)
            : await _httpClient.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);
        return await ProcessResponseAsync(response);
    }

    private async Task<JsonElement> PutJsonAsync(string url, object body)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(url, body, JsonOptions);
        return await ProcessResponseAsync(response);
    }

    private async Task<JsonElement> DeleteJsonAsync(string url)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(url);
        return await ProcessResponseAsync(response);
    }

    private async Task<JsonElement> ProcessResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return await ReadBodyAsync(response);

        string detail = await TryReadErrorDetailAsync(response);
        throw new HttpRequestException(detail);
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default;
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private static async Task<string> TryReadErrorDetailAsync(HttpResponseMessage response)
    {
        try
        {
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json") != true)
                return FormatStatus(response);

            JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            if (json.TryGetProperty("detail", out JsonElement d))
                return d.GetString() ?? FormatStatus(response);
            if (json.TryGetProperty("title", out JsonElement t))
                return t.GetString() ?? FormatStatus(response);
        }
        catch
        {
            // ignore parse errors
        }
        return FormatStatus(response);
    }

    private static string FormatStatus(HttpResponseMessage response) =>
        $"HTTP {(int)response.StatusCode} {response.StatusCode}";

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>Result of GET /api/twilio/status — drives the mock/live banner in Settings.</summary>
public sealed class TwilioStatusInfo
{
    public bool Mock { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string AccountSidPrefix { get; set; } = string.Empty;
    public bool HasMessagingService { get; set; }
    public string? Warning { get; set; }
}
