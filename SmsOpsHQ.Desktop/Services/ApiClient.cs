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

    // ── Messages ─────────────────────────────────────────────────────

    public async Task<JsonElement> SendMessageAsync(SendMessageRequest request)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/send", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> CreateNoteAsync(int threadId, CreateNoteRequest request)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"/api/thread/{threadId}/notes", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

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

    public async Task<JsonElement> UpdateCustomerAsync(int customerId, UpdateCustomerRequest request)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"/api/customer/{customerId}/update", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> GetLateCustomersAsync() =>
        await GetJsonAsync("/api/customers/late");

    public async Task<JsonElement> GetPfxCustomersAsync(int days = 60) =>
        await GetJsonAsync($"/api/customers/pfx?days={days}");

    public async Task<JsonElement> GetXpdCustomerByPhoneAsync(string phone) =>
        await GetJsonAsync($"/api/xpd/customer-by-phone?phone={Uri.EscapeDataString(phone)}");

    public async Task<JsonElement> AppendNoteXpdAsync(int customerKey, string note)
    {
        var request = new { customerKey, note };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/customers/append-note-xpd", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> TestSqliteAsync() =>
        await GetJsonAsync("/api/test-sqlite");

    // ── Templates ────────────────────────────────────────────────────

    public async Task<JsonElement> GetTemplatesAsync(int storeId) =>
        await GetJsonAsync($"/api/templates?store_id={storeId}");

    public async Task<JsonElement> CreateTemplateAsync(TemplateCreateRequest request)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/templates", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> UpdateTemplateAsync(int templateId, TemplateCreateRequest request)
    {
        HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"/api/templates/{templateId}", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> DeleteTemplateAsync(int templateId) =>
        await DeleteJsonAsync($"/api/templates/{templateId}");

    // ── Stores ───────────────────────────────────────────────────────

    public async Task<JsonElement> GetTwilioNumbersAsync(int storeId) =>
        await GetJsonAsync($"/api/stores/{storeId}/numbers");

    public async Task<JsonElement> AddNumberAsync(int storeId, string phone)
    {
        var request = new { phone };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"/api/stores/{storeId}/numbers", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> SetDefaultNumberAsync(int storeId, int numberId)
    {
        var request = new { numberId };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"/api/stores/{storeId}/default-number", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> UpdateNumberAsync(int storeId, int numberId, string phone)
    {
        var request = new { phone };
        HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"/api/stores/{storeId}/numbers/{numberId}", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> DeleteNumberAsync(int storeId, int numberId) =>
        await DeleteJsonAsync($"/api/stores/{storeId}/numbers/{numberId}");

    public async Task<JsonElement> UpdateTwilioConfigAsync(int storeId, string? sid, string? token)
    {
        var request = new { sid, token };
        HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"/api/stores/{storeId}/twilio_config", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    // ── Reminders ────────────────────────────────────────────────────

    public async Task<JsonElement> SendReminderAsync(int ticketKey, int customerKey, string phone, string transNo, string dueDate, int daysDiff)
    {
        var request = new { ticketKey, customerKey, phone, transNo, dueDate, daysDiff };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/reminders/send", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> RunBatchRemindersAsync(int maxCount = 100)
    {
        var request = new { maxCount };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/reminders/batch", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> RunAutoRemindersAsync()
    {
        HttpResponseMessage response = await _httpClient.PostAsync("/api/reminders/auto", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

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
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/reminders/exclude", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> UnsubscribePhoneAsync(string phone, string method = "MANUAL", string? notes = null)
    {
        var request = new { phone, method, notes };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/reminders/unsubscribe", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> CheckExcludedAsync(string phone) =>
        await GetJsonAsync($"/api/reminders/excluded/{Uri.EscapeDataString(phone)}");

    // ── Sync ─────────────────────────────────────────────────────────

    public async Task<JsonElement> GetSyncStatusAsync() =>
        await GetJsonAsync("/api/sync/status");

    public async Task<JsonElement> GetSyncProgressAsync() =>
        await GetJsonAsync("/api/sync/progress");

    public async Task<JsonElement> TriggerSyncAsync()
    {
        HttpResponseMessage response = await _httpClient.PostAsync("/api/sync/full", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> GetSyncCountsAsync() =>
        await GetJsonAsync("/api/sync/counts");

    // ── Quarantine ───────────────────────────────────────────────────

    public async Task<JsonElement> GetQuarantinedAsync(int limit = 50) =>
        await GetJsonAsync($"/api/quarantine?limit={limit}");

    public async Task<JsonElement> ResolveQuarantineAsync(int quarantineId, string resolution)
    {
        var request = new { resolution };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"/api/quarantine/{quarantineId}/resolve", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
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

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<JsonElement> DeleteJsonAsync(string url)
    {
        HttpResponseMessage response = await _httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public void Dispose() => _httpClient.Dispose();
}
