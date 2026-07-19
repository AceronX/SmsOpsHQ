using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Xunit;

namespace SmsOpsHQ.Tests;

[Collection("Integration")]
public sealed class LateTicketPullIntegrationTests : IntegrationTestBase
{
    public LateTicketPullIntegrationTests(IntegrationTestFixture factory) : base(factory)
    {
    }

    [Fact]
    public async Task PullingOneTicket_HidesOnlyThatTicket_IsIdempotent_AndRestoreReturnsIt()
    {
        (int storeId, int customerKey, int firstTicketKey, int secondTicketKey) =
            await SeedLateTicketsAsync();

        JsonElement firstPull = await PostJsonAsync(
            "/api/late-customers/pull-list",
            new { storeId, ticketKey = firstTicketKey, customerKey, reason = "Call later" });
        JsonElement repeatedPull = await PostJsonAsync(
            "/api/late-customers/pull-list",
            new { storeId, ticketKey = firstTicketKey, customerKey, reason = "Duplicate" });

        Assert.Equal(
            firstPull.GetProperty("late_ticket_pull_id").GetInt32(),
            repeatedPull.GetProperty("late_ticket_pull_id").GetInt32());

        JsonElement mainList = await PostJsonAsync("/api/customers/late", new { });
        Assert.DoesNotContain(mainList.EnumerateArray(), row => TicketKey(row) == firstTicketKey);
        Assert.Contains(mainList.EnumerateArray(), row => TicketKey(row) == secondTicketKey);

        JsonElement allRows = await PostJsonAsync(
            "/api/customers/late",
            new { includePulled = true });
        JsonElement pulledRow = allRows.EnumerateArray().Single(row => TicketKey(row) == firstTicketKey);
        JsonElement sameCustomerRow = allRows.EnumerateArray().Single(row => TicketKey(row) == secondTicketKey);
        Assert.True(pulledRow.GetProperty("is_on_pull_list").GetBoolean());
        Assert.False(sameCustomerRow.GetProperty("is_on_pull_list").GetBoolean());

        JsonElement pullList = await GetJsonAsync(
            $"/api/late-customers/pull-list?storeId={storeId}");
        Assert.Contains(pullList.EnumerateArray(), row =>
            row.GetProperty("ticket_key").GetInt32() == firstTicketKey);

        HttpResponseMessage restore = await DeleteAsync(
            $"/api/late-customers/pull-list/{firstTicketKey}?storeId={storeId}");
        HttpResponseMessage repeatedRestore = await DeleteAsync(
            $"/api/late-customers/pull-list/{firstTicketKey}?storeId={storeId}");
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedRestore.StatusCode);

        JsonElement restoredMainList = await PostJsonAsync("/api/customers/late", new { });
        Assert.Contains(restoredMainList.EnumerateArray(), row => TicketKey(row) == firstTicketKey);
        Assert.Contains(restoredMainList.EnumerateArray(), row => TicketKey(row) == secondTicketKey);
    }

    [Fact]
    public async Task CrossStoreUser_CannotPullOrRestoreTicket()
    {
        (int targetStoreId, int customerKey, int ticketKey, _) = await SeedLateTicketsAsync();
        string unique = Guid.NewGuid().ToString("N");
        const string password = "PullListTest!123";
        (int otherStoreId, string username) = await SeedOtherStoreUserAsync(unique, password);
        Assert.NotEqual(targetStoreId, otherStoreId);
        await AuthenticateAsUserAsync(username, password);

        HttpResponseMessage pull = await PostAsync(
            "/api/late-customers/pull-list",
            new { storeId = targetStoreId, ticketKey, customerKey });
        HttpResponseMessage restore = await DeleteAsync(
            $"/api/late-customers/pull-list/{ticketKey}?storeId={targetStoreId}");
        HttpResponseMessage get = await GetAsync(
            $"/api/late-customers/pull-list?storeId={targetStoreId}");

        Assert.Equal(HttpStatusCode.Forbidden, pull.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restore.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task PullTicket_WithWrongCustomerContext_IsRejected()
    {
        (int storeId, int customerKey, int ticketKey, _) = await SeedLateTicketsAsync();

        HttpResponseMessage response = await PostAsync(
            "/api/late-customers/pull-list",
            new { storeId, ticketKey, customerKey = customerKey + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement pullList = await GetJsonAsync(
            $"/api/late-customers/pull-list?storeId={storeId}");
        Assert.DoesNotContain(pullList.EnumerateArray(), row =>
            row.GetProperty("ticket_key").GetInt32() == ticketKey);
    }

    private async Task<(int StoreId, int CustomerKey, int FirstTicketKey, int SecondTicketKey)>
        SeedLateTicketsAsync()
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        StoreEntity? store = await db.Stores.OrderBy(candidate => candidate.StoreId).FirstOrDefaultAsync();
        if (store is null)
        {
            store = new StoreEntity
            {
                StoreName = $"Pull Store {Guid.NewGuid():N}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Stores.Add(store);
            await db.SaveChangesAsync();
        }

        int customerKey = (await db.Customers.MaxAsync(customer => (int?)customer.CustomerKey) ?? 940000) + 1;
        int firstTicketKey = (await db.Tickets.MaxAsync(ticket => (int?)ticket.Key) ?? 950000) + 1;
        int secondTicketKey = firstTicketKey + 1;
        db.Customers.Add(new CustomerEntity
        {
            StoreId = store.StoreId,
            CustomerKey = customerKey,
            PhoneE164 = $"+1555{customerKey % 10_000_000:D7}",
            FirstName = "Pull",
            LastName = "ListTest",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Tickets.AddRange(
            NewLateTicket(firstTicketKey, customerKey),
            NewLateTicket(secondTicketKey, customerKey));
        await db.SaveChangesAsync();
        return (store.StoreId, customerKey, firstTicketKey, secondTicketKey);
    }

    private async Task<(int StoreId, string Username)> SeedOtherStoreUserAsync(
        string unique,
        string password)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        StoreEntity store = new()
        {
            StoreName = $"Other Pull Store {unique}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        string username = $"pull_{unique}";
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
        return (store.StoreId, username);
    }

    private static TicketEntity NewLateTicket(int ticketKey, int customerKey) => new()
    {
        Key = ticketKey,
        CustomerKey = customerKey,
        TransNo = ticketKey,
        Type = 1,
        Active = 1,
        DueDate = "2000-01-01",
        Amount = 100,
        CurrentBalance = 50
    };

    private static int TicketKey(JsonElement row) => row.GetProperty("ticket_key").GetInt32();
}
