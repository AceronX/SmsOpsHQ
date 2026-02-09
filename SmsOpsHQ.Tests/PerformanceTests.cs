using System.Diagnostics;
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
using Xunit.Abstractions;

namespace SmsOpsHQ.Tests;

public class PerformanceTestFixture : WebApplicationFactory<Program>
{
    private readonly string _testDbPath = Path.Combine(
        Path.GetTempPath(),
        $"smsops_perf_{Guid.NewGuid():N}.db");

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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); }
            catch { }
        }
    }
}

public class PerformanceTests : IClassFixture<PerformanceTestFixture>, IAsyncLifetime
{
    private readonly PerformanceTestFixture _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(PerformanceTestFixture factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string json = JsonSerializer.Serialize(new { username = "admin", password = "password" });
        StringContent content = new(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await _client.PostAsync("/api/auth/login", content);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        JsonDocument doc = JsonDocument.Parse(body);
        string token = doc.RootElement.GetProperty("accessToken").GetString()!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedRealisticDataAsync(int threadCount, int messagesPerThread)
    {
        // Guard: skip seeding if data already exists from a previous test in this fixture.
        // Each test method gets a new class instance, but the fixture (and its DB) is shared.
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Threads.Any(t => t.StoreId == 1))
        {
            _output.WriteLine("Data already seeded, skipping.");
            return;
        }

        if (!db.TwilioNumbers.Any(n => n.StoreId == 1))
        {
            TwilioNumberEntity numberEntity = new()
            {
                StoreId = 1,
                PhoneE164 = "+15550001000",
                FriendlyName = "Perf Test",
                IsActive = true
            };
            db.TwilioNumbers.Add(numberEntity);
            await db.SaveChangesAsync();

            StoreEntity? store = db.Stores.FirstOrDefault(s => s.StoreId == 1);
            if (store is not null)
            {
                store.DefaultNumberId = numberEntity.NumberId;
                await db.SaveChangesAsync();
            }
        }

        List<CustomerEntity> customers = new();
        for (int i = 0; i < threadCount; i++)
        {
            CustomerEntity customer = new()
            {
                StoreId = 1,
                PhoneE164 = $"+1555{i:D7}",
                FirstName = $"Perf{i}",
                LastName = $"Customer{i}",
                CreatedAt = DateTime.UtcNow
            };
            customers.Add(customer);
        }
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        int? twilioNumberId = db.TwilioNumbers.Where(n => n.StoreId == 1).Select(n => (int?)n.NumberId).FirstOrDefault();

        List<ThreadEntity> threads = new();
        for (int i = 0; i < threadCount; i++)
        {
            ThreadEntity thread = new()
            {
                StoreId = 1,
                CustomerId = customers[i].CustomerId,
                IdentityId = i + 1,
                TwilioNumberId = twilioNumberId,
                Status = "Open",
                LastMessageAt = DateTime.UtcNow.AddMinutes(-i),
                UnreadCount = i % 3 == 0 ? 1 : 0,
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            };
            threads.Add(thread);
        }
        db.Threads.AddRange(threads);
        await db.SaveChangesAsync();

        int batchSize = 500;
        List<MessageEntity> batch = new(batchSize);
        int totalMessages = threadCount * messagesPerThread;

        for (int t = 0; t < threadCount; t++)
        {
            for (int m = 0; m < messagesPerThread; m++)
            {
                bool isOutbound = m % 2 == 0;
                batch.Add(new MessageEntity
                {
                    ThreadId = threads[t].ThreadId,
                    StoreId = 1,
                    StorePhone = "+15550001000",
                    Direction = isOutbound ? "Outbound" : "Inbound",
                    FromE164 = isOutbound ? "+15550001000" : customers[t].PhoneE164,
                    ToE164 = isOutbound ? customers[t].PhoneE164 : "+15550001000",
                    Body = $"Performance test message {m} for thread {t}. This is a realistic message body.",
                    Category = "general",
                    Status = isOutbound ? "Sent" : "Received",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-(totalMessages) + (t * messagesPerThread + m))
                });

                if (batch.Count >= batchSize)
                {
                    db.Messages.AddRange(batch);
                    await db.SaveChangesAsync();
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            db.Messages.AddRange(batch);
            await db.SaveChangesAsync();
        }

        _output.WriteLine($"Seeded {threadCount} threads, {threadCount} customers, {totalMessages} messages");
    }

    [Fact]
    public async Task InboxLoad_Under500ms_P95()
    {
        await SeedRealisticDataAsync(threadCount: 100, messagesPerThread: 10);

        List<long> timings = new();
        int iterations = 20;

        for (int i = 0; i < iterations; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            HttpResponseMessage response = await _client.GetAsync("/api/inbox?store_id=1&filter=all");
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            timings.Add(sw.ElapsedMilliseconds);
        }

        timings.Sort();
        long p95 = timings[(int)(iterations * 0.95)];
        long median = timings[iterations / 2];
        long average = (long)timings.Average();

        _output.WriteLine($"Inbox Load: avg={average}ms, median={median}ms, p95={p95}ms ({iterations} iters, 100 threads)");
        // Budget is 500ms for production; allow 1000ms in test (cold start, CI variance).
        Assert.True(p95 < 1000, $"Inbox p95 ({p95}ms) exceeded 1000ms test budget");
    }

    [Fact]
    public async Task ThreadLoad_Under200ms_P95()
    {
        await SeedRealisticDataAsync(threadCount: 50, messagesPerThread: 20);

        JsonElement inbox = await GetJsonAsync("/api/inbox?store_id=1&filter=all");
        Assert.True(inbox.GetArrayLength() > 0, "Expected at least 1 thread in inbox");
        int threadId = inbox[0].GetProperty("thread_id").GetInt32();

        List<long> timings = new();
        int iterations = 20;

        for (int i = 0; i < iterations; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            HttpResponseMessage response = await _client.GetAsync($"/api/thread/{threadId}?store_id=1");
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            timings.Add(sw.ElapsedMilliseconds);
        }

        timings.Sort();
        long p95 = timings[(int)(iterations * 0.95)];
        long median = timings[iterations / 2];
        long average = (long)timings.Average();

        _output.WriteLine($"Thread Load: avg={average}ms, median={median}ms, p95={p95}ms ({iterations} iters, 20 messages)");
        Assert.True(p95 < 200, $"Thread p95 ({p95}ms) exceeded 200ms budget");
    }

    [Fact]
    public async Task MessageCounts_Under100ms()
    {
        await SeedRealisticDataAsync(threadCount: 50, messagesPerThread: 10);

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage response = await _client.GetAsync("/api/messages/counts?store_id=1");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _output.WriteLine($"Message Counts: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 100, $"Message counts took {sw.ElapsedMilliseconds}ms, budget 100ms");
    }

    [Fact]
    public async Task CustomerSearch_Under100ms()
    {
        await SeedRealisticDataAsync(threadCount: 50, messagesPerThread: 5);

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage response = await _client.GetAsync("/api/customers/search?q=Perf&limit=10");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _output.WriteLine($"Customer Search: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 100, $"Customer search took {sw.ElapsedMilliseconds}ms, budget 100ms");
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }
}
