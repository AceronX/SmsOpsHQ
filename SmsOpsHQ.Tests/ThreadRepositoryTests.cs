using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;
using Thread = SmsOpsHQ.Core.Entities.Thread;

namespace SmsOpsHQ.Tests;

// Integration tests for ThreadRepository against an in-memory SQLite database.
public class ThreadRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ThreadRepository _repo;
    private readonly int _storeId;
    private readonly int _twilioNumberId;

    public ThreadRepositoryTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        StoreEntity store = new StoreEntity { StoreName = "Test Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity number = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15550000001",
            IsActive = true
        };
        _db.TwilioNumbers.Add(number);
        _db.SaveChanges();
        _twilioNumberId = number.NumberId;

        _repo = new ThreadRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── FindOrCreateAsync ────────────────────────────────────────────

    [Fact]
    public async Task FindOrCreateAsync_NewThread_CreatesIt()
    {
        Thread thread = await CreateThreadAsync(identityId: 42);

        Assert.True(thread.ThreadId > 0);
        Assert.Equal(_storeId, thread.StoreId);
        Assert.Equal(42, thread.IdentityId);
        Assert.Equal(_twilioNumberId, thread.TwilioNumberId);
        Assert.Equal("+15551110001", thread.ContactPhoneE164);
        Assert.Equal("Open", thread.Status);
        Assert.Equal(0, thread.UnreadCount);
    }

    [Fact]
    public async Task FindOrCreateAsync_ExistingThread_ReturnsExisting()
    {
        Thread created = await CreateThreadAsync(identityId: 42);
        Thread found = await CreateThreadAsync(identityId: 99);

        Assert.Equal(created.ThreadId, found.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_SameIdentityDifferentPhone_CreatesSeparateThreads()
    {
        Thread t1 = await CreateThreadAsync(identityId: 42, phone: "+15551110001");
        Thread t2 = await CreateThreadAsync(identityId: 42, phone: "+15551110002");

        Assert.NotEqual(t1.ThreadId, t2.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_SameCustomerDifferentPhone_CreatesSeparateThreads()
    {
        CustomerEntity customer = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15551110003"
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        Thread t1 = await CreateThreadAsync(
            identityId: null, customerId: customer.CustomerId, phone: "+15551110003");
        Thread t2 = await CreateThreadAsync(
            identityId: null, customerId: customer.CustomerId, phone: "+15551110004");

        Assert.NotEqual(t1.ThreadId, t2.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_SamePhoneDifferentStoreNumber_CreatesSeparateThreads()
    {
        TwilioNumberEntity secondNumber = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15550000002",
            IsActive = true
        };
        _db.TwilioNumbers.Add(secondNumber);
        await _db.SaveChangesAsync();

        Thread t1 = await CreateThreadAsync(identityId: 42, phone: "+15551110005");
        Thread t2 = await CreateThreadAsync(
            identityId: 42, phone: "+15551110005", numberId: secondNumber.NumberId);

        Assert.NotEqual(t1.ThreadId, t2.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_DifferentStores_CreatesSeparateThreads()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        Thread t1 = await CreateThreadAsync(identityId: 42);
        Thread t2 = await _repo.FindOrCreateAsync(
            store2.StoreId, _twilioNumberId, "+15551110001", 42, null);

        Assert.NotEqual(t1.ThreadId, t2.ThreadId);
    }

    // ── GetInboxAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetInboxAsync_ReturnsSortedByLastMessageAtDesc()
    {
        _db.Threads.Add(new ThreadEntity
        {
            StoreId = _storeId, Status = "Open",
            LastMessageAt = new DateTime(2026, 1, 1)
        });
        _db.Threads.Add(new ThreadEntity
        {
            StoreId = _storeId, Status = "Open",
            LastMessageAt = new DateTime(2026, 2, 1)
        });
        _db.Threads.Add(new ThreadEntity
        {
            StoreId = _storeId, Status = "Open",
            LastMessageAt = new DateTime(2026, 1, 15)
        });
        _db.SaveChanges();

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, null, null, null);

        Assert.Equal(3, inbox.Count);
        Assert.True(inbox[0].LastMessageAt > inbox[1].LastMessageAt);
        Assert.True(inbox[1].LastMessageAt > inbox[2].LastMessageAt);
    }

    [Fact]
    public async Task GetInboxAsync_FilterUnread_OnlyReturnsUnread()
    {
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open", UnreadCount = 0 });
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open", UnreadCount = 3 });
        _db.SaveChanges();

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, "unread", null, null);

        Assert.Single(inbox);
        Assert.Equal(3, inbox[0].UnreadCount);
    }

    [Fact]
    public async Task GetInboxAsync_FilterOpen_OnlyReturnsOpen()
    {
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open" });
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Closed" });
        _db.SaveChanges();

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, "open", null, null);

        Assert.Single(inbox);
        Assert.Equal("Open", inbox[0].Status);
    }

    [Fact]
    public async Task GetInboxAsync_FilterClosed_OnlyReturnsClosed()
    {
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open" });
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Closed" });
        _db.SaveChanges();

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, "closed", null, null);

        Assert.Single(inbox);
        Assert.Equal("Closed", inbox[0].Status);
    }

    [Fact]
    public async Task GetInboxAsync_StoreIsolation_OnlyReturnsOwnStore()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open" });
        _db.Threads.Add(new ThreadEntity { StoreId = store2.StoreId, Status = "Open" });
        _db.SaveChanges();

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, null, null, null);
        Assert.Single(inbox);
    }

    [Fact]
    public async Task GetInboxAsync_Search_FiltersByCustomerNameOrPhone()
    {
        CustomerEntity cust1 = new CustomerEntity { StoreId = _storeId, PhoneE164 = "+15551234567", FirstName = "Alice", LastName = "Smith" };
        CustomerEntity cust2 = new CustomerEntity { StoreId = _storeId, PhoneE164 = "+15559876543", FirstName = "Bob", LastName = "Jones" };
        _db.Customers.Add(cust1);
        _db.Customers.Add(cust2);
        _db.SaveChanges();

        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open", CustomerId = cust1.CustomerId, LastMessageAt = DateTime.UtcNow });
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open", CustomerId = cust2.CustomerId, LastMessageAt = DateTime.UtcNow.AddMinutes(-1) });
        _db.SaveChanges();

        List<Thread> inboxAll = await _repo.GetInboxAsync(_storeId, null, null, null);
        Assert.Equal(2, inboxAll.Count);

        List<Thread> inboxSearchAlice = await _repo.GetInboxAsync(_storeId, null, "Alice", null);
        Assert.Single(inboxSearchAlice);
        Assert.Equal(cust1.CustomerId, inboxSearchAlice[0].CustomerId);

        List<Thread> inboxSearchJones = await _repo.GetInboxAsync(_storeId, null, "Jones", null);
        Assert.Single(inboxSearchJones);
        Assert.Equal(cust2.CustomerId, inboxSearchJones[0].CustomerId);

        List<Thread> inboxSearchPhone = await _repo.GetInboxAsync(_storeId, null, "9876543", null);
        Assert.Single(inboxSearchPhone);
        Assert.Equal(cust2.CustomerId, inboxSearchPhone[0].CustomerId);

        List<Thread> inboxSearchNoMatch = await _repo.GetInboxAsync(_storeId, null, "Nobody", null);
        Assert.Empty(inboxSearchNoMatch);
    }

    [Fact]
    public async Task GetInboxWithCustomersAsync_ReturnsThreadsWithCustomerData()
    {
        CustomerEntity cust = new CustomerEntity { StoreId = _storeId, PhoneE164 = "+15551112222", FirstName = "Jane", LastName = "Doe" };
        _db.Customers.Add(cust);
        _db.SaveChanges();
        _db.Threads.Add(new ThreadEntity { StoreId = _storeId, Status = "Open", CustomerId = cust.CustomerId, LastMessageAt = DateTime.UtcNow });
        _db.SaveChanges();

        var rows = await _repo.GetInboxWithCustomersAsync(_storeId, null, null, null);

        Assert.Single(rows);
        Assert.Equal(cust.CustomerId, rows[0].thread.CustomerId);
        Customer? c = rows[0].customer;
        Assert.NotNull(c);
        Assert.Equal("Jane", c.FirstName);
        Assert.Equal("Doe", c.LastName);
        Assert.Equal("+15551112222", c.PhoneE164);
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsThread()
    {
        Thread created = await CreateThreadAsync(identityId: 10);
        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);

        Assert.NotNull(found);
        Assert.Equal(created.ThreadId, found.ThreadId);
    }

    [Fact]
    public async Task GetByIdAsync_WrongStore_ReturnsNull()
    {
        Thread created = await CreateThreadAsync(identityId: 10);
        Thread? found = await _repo.GetByIdAsync(99999, created.ThreadId);

        Assert.Null(found);
    }

    // ── UpdateLastMessageAtAsync ─────────────────────────────────────

    [Fact]
    public async Task UpdateLastMessageAtAsync_UpdatesTimestamp()
    {
        Thread created = await CreateThreadAsync(identityId: 10);
        DateTime now = new DateTime(2026, 2, 6, 12, 0, 0, DateTimeKind.Utc);

        await _repo.UpdateLastMessageAtAsync(created.ThreadId, now);

        Thread? updated = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.NotNull(updated);
        Assert.Equal(now, updated.LastMessageAt);
    }

    // ── IncrementUnreadAsync ─────────────────────────────────────────

    [Fact]
    public async Task IncrementUnreadAsync_IncrementsBy1()
    {
        Thread created = await CreateThreadAsync(identityId: 10);
        Assert.Equal(0, created.UnreadCount);

        await _repo.IncrementUnreadAsync(created.ThreadId);
        await _repo.IncrementUnreadAsync(created.ThreadId);

        Thread? updated = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.UnreadCount);
    }

    // ── MarkReadAsync ────────────────────────────────────────────────

    [Fact]
    public async Task MarkReadAsync_ResetsUnreadToZero()
    {
        Thread created = await CreateThreadAsync(identityId: 10);
        await _repo.IncrementUnreadAsync(created.ThreadId);
        await _repo.IncrementUnreadAsync(created.ThreadId);
        await _repo.IncrementUnreadAsync(created.ThreadId);

        await _repo.MarkReadAsync(created.ThreadId);

        Thread? updated = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.NotNull(updated);
        Assert.Equal(0, updated.UnreadCount);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesThread()
    {
        Thread created = await CreateThreadAsync(identityId: 10);

        await _repo.DeleteAsync(_storeId, created.ThreadId);

        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_WrongStore_DoesNotDelete()
    {
        Thread created = await CreateThreadAsync(identityId: 10);

        await _repo.DeleteAsync(99999, created.ThreadId);

        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.NotNull(found);
    }

    // ── DeleteAllAsync ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_RemovesAllForStore()
    {
        await CreateThreadAsync(identityId: 10, phone: "+15551110010");
        await CreateThreadAsync(identityId: 20, phone: "+15551110020");
        await CreateThreadAsync(identityId: 30, phone: "+15551110030");

        await _repo.DeleteAllAsync(_storeId);

        List<Thread> inbox = await _repo.GetInboxAsync(_storeId, null, null, null);
        Assert.Empty(inbox);
    }

    [Fact]
    public async Task DeleteAllAsync_DoesNotAffectOtherStores()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        await CreateThreadAsync(identityId: 10);
        _db.Threads.Add(new ThreadEntity { StoreId = store2.StoreId, Status = "Open" });
        _db.SaveChanges();

        await _repo.DeleteAllAsync(_storeId);

        List<Thread> store2Inbox = await _repo.GetInboxAsync(store2.StoreId, null, null, null);
        Assert.Single(store2Inbox);
    }

    private Task<Thread> CreateThreadAsync(
        int? identityId,
        string phone = "+15551110001",
        int? customerId = null,
        int? numberId = null)
    {
        return _repo.FindOrCreateAsync(
            _storeId,
            numberId ?? _twilioNumberId,
            phone,
            identityId,
            customerId);
    }
}
