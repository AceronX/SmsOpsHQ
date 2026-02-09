using Microsoft.EntityFrameworkCore;
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

    public ThreadRepositoryTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        StoreEntity store = new StoreEntity { StoreName = "Test Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

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
        Thread thread = await _repo.FindOrCreateAsync(_storeId, identityId: 42);

        Assert.True(thread.ThreadId > 0);
        Assert.Equal(_storeId, thread.StoreId);
        Assert.Equal(42, thread.IdentityId);
        Assert.Equal("Open", thread.Status);
        Assert.Equal(0, thread.UnreadCount);
    }

    [Fact]
    public async Task FindOrCreateAsync_ExistingThread_ReturnsExisting()
    {
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 42);
        Thread found = await _repo.FindOrCreateAsync(_storeId, identityId: 42);

        Assert.Equal(created.ThreadId, found.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_NullIdentity_AlwaysCreatesNew()
    {
        Thread t1 = await _repo.FindOrCreateAsync(_storeId, identityId: null);
        Thread t2 = await _repo.FindOrCreateAsync(_storeId, identityId: null);

        Assert.NotEqual(t1.ThreadId, t2.ThreadId);
    }

    [Fact]
    public async Task FindOrCreateAsync_DifferentStores_CreatesSeparateThreads()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        Thread t1 = await _repo.FindOrCreateAsync(_storeId, identityId: 42);
        Thread t2 = await _repo.FindOrCreateAsync(store2.StoreId, identityId: 42);

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

    // ── GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsThread()
    {
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);
        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);

        Assert.NotNull(found);
        Assert.Equal(created.ThreadId, found.ThreadId);
    }

    [Fact]
    public async Task GetByIdAsync_WrongStore_ReturnsNull()
    {
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);
        Thread? found = await _repo.GetByIdAsync(99999, created.ThreadId);

        Assert.Null(found);
    }

    // ── UpdateLastMessageAtAsync ─────────────────────────────────────

    [Fact]
    public async Task UpdateLastMessageAtAsync_UpdatesTimestamp()
    {
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);
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
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);
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
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);
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
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);

        await _repo.DeleteAsync(_storeId, created.ThreadId);

        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_WrongStore_DoesNotDelete()
    {
        Thread created = await _repo.FindOrCreateAsync(_storeId, identityId: 10);

        await _repo.DeleteAsync(99999, created.ThreadId);

        Thread? found = await _repo.GetByIdAsync(_storeId, created.ThreadId);
        Assert.NotNull(found);
    }

    // ── DeleteAllAsync ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_RemovesAllForStore()
    {
        await _repo.FindOrCreateAsync(_storeId, identityId: 10);
        await _repo.FindOrCreateAsync(_storeId, identityId: 20);
        await _repo.FindOrCreateAsync(_storeId, identityId: 30);

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

        await _repo.FindOrCreateAsync(_storeId, identityId: 10);
        _db.Threads.Add(new ThreadEntity { StoreId = store2.StoreId, Status = "Open" });
        _db.SaveChanges();

        await _repo.DeleteAllAsync(_storeId);

        List<Thread> store2Inbox = await _repo.GetInboxAsync(store2.StoreId, null, null, null);
        Assert.Single(store2Inbox);
    }
}
