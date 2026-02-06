using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

// Shared HTTP client configured with the API base URL.
// Used by all windows to communicate with SmsOpsHQ.Api.
// Created once at application startup and disposed on exit.
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    // Shared JSON options matching the API's camelCase naming convention.
    public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // The configured API base URL (e.g. "http://localhost:5000/").
    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;

    // The underlying HttpClient for making API requests.
    public HttpClient Http => _httpClient;

    // Sets the Authorization header to "Bearer {accessToken}" for authenticated requests.
    public void SetAuthToken(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }

    // Clears the Authorization header (e.g. on logout).
    public void ClearAuthToken()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
