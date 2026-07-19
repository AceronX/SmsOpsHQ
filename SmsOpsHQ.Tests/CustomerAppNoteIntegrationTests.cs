using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public sealed class CustomerAppNoteIntegrationTests : IntegrationTestBase
{
    public CustomerAppNoteIntegrationTests(IntegrationTestFixture factory) : base(factory)
    {
    }

    [Fact]
    public async Task AppNote_PersistsAcrossApiReload()
    {
        int customerKey = await SeedCustomerAsync("Persistence");

        JsonElement created = await PostJsonAsync(
            $"/api/customers/{customerKey}/app-notes",
            new { content = "  Follow up on Friday.  " });

        Assert.Equal("Follow up on Friday.", created.GetProperty("content").GetString());
        Assert.True(created.GetProperty("customerAppNoteId").GetInt32() > 0);

        JsonElement reloaded = await GetJsonAsync($"/api/customers/{customerKey}/app-notes");
        JsonElement saved = Assert.Single(reloaded.EnumerateArray());
        Assert.Equal("Follow up on Friday.", saved.GetProperty("content").GetString());
        Assert.False(string.IsNullOrWhiteSpace(saved.GetProperty("createdByUsername").GetString()));
    }

    [Fact]
    public async Task XpdMirrorRefresh_DoesNotDeleteAppNotes()
    {
        int customerKey = await SeedCustomerAsync("XpdRefresh");
        await PostJsonAsync(
            $"/api/customers/{customerKey}/app-notes",
            new { content = "App-owned note" });

        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            CustomerEntity customer = db.Customers.Single(c => c.CustomerKey == customerKey);
            customer.Notes = "Replacement notes imported from XPD";
            customer.SyncedAt = DateTime.UtcNow.ToString("O");
            await db.SaveChangesAsync();
        }

        JsonElement reloaded = await GetJsonAsync($"/api/customers/{customerKey}/app-notes");
        Assert.Equal("App-owned note", Assert.Single(reloaded.EnumerateArray()).GetProperty("content").GetString());
    }

    [Fact]
    public async Task CrossStoreUser_CannotReadOrCreateAppNotes()
    {
        (string username, string password) = await SeedStoreUserAsync("AppNoteUser");
        int otherStoreCustomerKey = await SeedCustomerAsync("OtherStore");
        await AuthenticateAsUserAsync(username, password);

        HttpResponseMessage getResponse = await GetAsync(
            $"/api/customers/{otherStoreCustomerKey}/app-notes");
        HttpResponseMessage postResponse = await PostAsync(
            $"/api/customers/{otherStoreCustomerKey}/app-notes",
            new { content = "Not allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    public async Task EmptyAndOverlengthAppNotes_AreRejected()
    {
        int customerKey = await SeedCustomerAsync("Validation");

        HttpResponseMessage empty = await PostAsync(
            $"/api/customers/{customerKey}/app-notes",
            new { content = "   " });
        HttpResponseMessage overlength = await PostAsync(
            $"/api/customers/{customerKey}/app-notes",
            new { content = new string('x', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overlength.StatusCode);

        JsonElement notes = await GetJsonAsync($"/api/customers/{customerKey}/app-notes");
        Assert.Empty(notes.EnumerateArray());
    }

    private async Task<int> SeedCustomerAsync(string suffix)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int customerKey = (db.Customers.Max(customer => (int?)customer.CustomerKey) ?? 850000) + 1;
        int storeId = db.Stores.OrderBy(store => store.StoreId).Select(store => store.StoreId).First();
        db.Customers.Add(new CustomerEntity
        {
            StoreId = storeId,
            CustomerKey = customerKey,
            PhoneE164 = $"+1555{customerKey:D7}",
            FirstName = suffix,
            LastName = "AppNoteTest",
            Notes = "Read-only XPD note",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return customerKey;
    }

    private async Task<(string Username, string Password)> SeedStoreUserAsync(string suffix)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string unique = Guid.NewGuid().ToString("N");
        StoreEntity store = new()
        {
            StoreName = $"{suffix}-{unique}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        string username = $"appnote_{unique}";
        const string password = "AppNoteTest!123";
        db.Users.Add(new UserEntity
        {
            StoreId = store.StoreId,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "StoreAdmin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (username, password);
    }
}
