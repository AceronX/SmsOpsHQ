using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class IntegrationTestFixture : WebApplicationFactory<Program>
{
    private readonly string _testDbPath = Path.Combine(
        Path.GetTempPath(),
        $"smsops_integ_{Guid.NewGuid():N}.db");

    private string? _cachedAdminToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((WebHostBuilderContext _, IConfigurationBuilder config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_testDbPath}"
            });
        });
    }

    public async Task<string> GetAdminTokenAsync()
    {
        if (_cachedAdminToken is not null)
            return _cachedAdminToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_cachedAdminToken is not null)
                return _cachedAdminToken;

            using HttpClient httpClient = CreateClient();
            string json = JsonSerializer.Serialize(new { username = "admin", password = "password" });
            StringContent content = new(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync("/api/auth/login", content);
            response.EnsureSuccessStatusCode();

            string body = await response.Content.ReadAsStringAsync();
            JsonDocument doc = JsonDocument.Parse(body);
            _cachedAdminToken = doc.RootElement.GetProperty("accessToken").GetString()!;
            return _cachedAdminToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task SeedStoreUserAsync(int storeId, string storeName, string username, string password, string role = "StoreAdmin")
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        bool storeExists = db.Stores.Any(s => s.StoreId == storeId);
        if (!storeExists)
        {
            db.Stores.Add(new StoreEntity
            {
                StoreName = storeName,
                Address = "100 Test St",
                City = "TestCity",
                State = "TX",
                Zip = "75000",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        bool userExists = db.Users.Any(u => u.Username == username);
        if (!userExists)
        {
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
            db.Users.Add(new UserEntity
            {
                StoreId = storeId,
                TwilioNumberId = null,
                Username = username,
                PasswordHash = passwordHash,
                Role = role,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task SeedTwilioNumberAsync(int storeId, string phone, bool isDefault = true)
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        bool exists = db.TwilioNumbers.Any(n => n.PhoneE164 == phone && n.StoreId == storeId);
        if (!exists)
        {
            TwilioNumberEntity numberEntity = new()
            {
                StoreId = storeId,
                PhoneE164 = phone,
                FriendlyName = "Test Number",
                IsActive = true
            };
            db.TwilioNumbers.Add(numberEntity);
            await db.SaveChangesAsync();

            if (isDefault)
            {
                StoreEntity? store = db.Stores.FirstOrDefault(s => s.StoreId == storeId);
                if (store is not null)
                {
                    store.DefaultNumberId = numberEntity.NumberId;
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _tokenLock.Dispose();
        if (disposing && File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); }
            catch { }
        }
    }
}

public abstract class IntegrationTestBase : IClassFixture<IntegrationTestFixture>, IAsyncLifetime
{
    protected readonly IntegrationTestFixture Factory;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(IntegrationTestFixture factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    public virtual async Task InitializeAsync()
    {
        await AuthenticateAsAdminAsync();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected async Task AuthenticateAsAdminAsync()
    {
        string token = await Factory.GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected async Task AuthenticateAsUserAsync(string username, string password)
    {
        using HttpClient loginClient = Factory.CreateClient();
        string json = JsonSerializer.Serialize(new { username, password });
        StringContent content = new(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await loginClient.PostAsync("/api/auth/login", content);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();
        JsonDocument doc = JsonDocument.Parse(body);
        string token = doc.RootElement.GetProperty("accessToken").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected void ClearAuth()
    {
        Client.DefaultRequestHeaders.Authorization = null;
    }

    protected async Task<JsonElement> PostJsonAsync(string url, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        StringContent content = new(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await Client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    protected async Task<JsonElement> GetJsonAsync(string url)
    {
        HttpResponseMessage response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    protected async Task<HttpResponseMessage> GetAsync(string url) => await Client.GetAsync(url);

    protected async Task<HttpResponseMessage> PostAsync(string url, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        StringContent content = new(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync(url, content);
    }

    protected async Task<HttpResponseMessage> PutAsync(string url, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        StringContent content = new(json, Encoding.UTF8, "application/json");
        return await Client.PutAsync(url, content);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string url) => await Client.DeleteAsync(url);

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
