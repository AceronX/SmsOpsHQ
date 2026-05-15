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
public sealed class ApiClient : IDisposable
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

    public async Task<JsonElement> GetLateCustomersAsync(string? query = null)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            var request = new { Query = query };
            return await PostJsonAsync("/api/customers/late", request);
        }
        return await PostJsonAsync("/api/customers/late", new { });
    }

    public async Task<JsonElement> GetPfxCustomersAsync(int days = 60) =>
        await GetJsonAsync($"/api/customers/pfx?days={days}");

    public async Task<JsonElement> GetCustomerByPhoneAsync(string phone, int? selectedCustomerKey = null)
    {
        string url = $"/api/customer/by-phone?phone={Uri.EscapeDataString(phone)}";
        if (selectedCustomerKey is int k)
            url += $"&selectedCustomerKey={k}";
        return await GetJsonAsync(url);
    }

    public async Task<byte[]?> GetCustomerIdPhotoBytesAsync(int customerKey)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"/api/customer/id-photo?customerKey={customerKey}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<JsonElement> AppendNoteXpdAsync(int customerKey, string note)
    {
        var request = new { customerKey, note };
        return await PostJsonAsync("/api/customers/append-note-xpd", request);
    }

    public async Task<JsonElement> GetCustomerQualityAsync(int customerKey, string qualityMetric = "default")
    {
        var request = new { customerKey, qualityMetric };
        return await PostJsonAsync("/api/customers/quality", request);
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

    public async Task<JsonElement> SendReviewRequestAsync(int storeId, string phone)
    {
        var request = new { storeId, customerPhone = phone };
        return await PostJsonAsync("/api/reviews/send", request);
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

    // ── HTTP helpers ─────────────────────────────────────────────────

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url);
        return await ProcessResponseAsync(response);
    }

    private async Task<JsonElement> PostJsonAsync(string url, object? body = null)
    {
        using HttpResponseMessage response = body is null
            ? await _httpClient.PostAsync(url, null)
            : await _httpClient.PostAsJsonAsync(url, body, JsonOptions);
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
