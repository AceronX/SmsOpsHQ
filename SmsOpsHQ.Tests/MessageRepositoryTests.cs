using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for MessageRepository against an in-memory SQLite database.
public class MessageRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MessageRepository _repo;
    private readonly int _storeId;
    private readonly int _threadId;
    private readonly int _userId;

    public MessageRepositoryTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        // Seed store
        StoreEntity store = new StoreEntity { StoreName = "Test Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        // Seed user
        UserEntity user = new UserEntity
        {
            Username = "testuser",
            PasswordHash = "hash",
            Role = "StoreAdmin",
            StoreId = _storeId,
            TwilioNumberId = null,
            IsActive = true
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _userId = user.UserId;

        // Seed thread
        ThreadEntity thread = new ThreadEntity
        {
            StoreId = _storeId,
            ContactPhoneE164 = "+17185550199",
            Status = "Open",
            UnreadCount = 0
        };
        _db.Threads.Add(thread);
        _db.SaveChanges();
        _threadId = thread.ThreadId;

        _repo = new MessageRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── CreateOutboundAsync ──────────────────────────────────────────

    [Fact]
    public async Task CreateOutboundAsync_CreatesMessage_WithCorrectFields()
    {
        Message msg = await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "Hello!",
            null, "general", _userId);

        Assert.True(msg.MessageId > 0);
        Assert.Equal("Outbound", msg.Direction);
        Assert.Equal("Queued", msg.Status);
        Assert.Equal("Hello!", msg.Body);
        Assert.Equal(_userId, msg.SentByUserId);
        Assert.Equal("general", msg.Category);
    }

    // ── CreateInboundAsync ───────────────────────────────────────────

    [Fact]
    public async Task CreateInboundAsync_CreatesMessage_WithCorrectFields()
    {
        Message msg = await _repo.CreateInboundAsync(
            _storeId, _threadId, "+19294990435",
            "+17185551234", "+19294990435", "Incoming!",
            null, "reminder");

        Assert.Equal("Inbound", msg.Direction);
        Assert.Equal("Received", msg.Status);
        Assert.Equal("reminder", msg.Category);
    }

    // ── FindBySidAsync ───────────────────────────────────────────────

    [Fact]
    public async Task FindBySidAsync_Exists_ReturnsMessage()
    {
        Message created = await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "Test",
            null, "general", _userId);

        await _repo.UpdateSentAsync(created.MessageId, "SM_TEST_123", "Sent");

        Message? found = await _repo.FindBySidAsync("SM_TEST_123");

        Assert.NotNull(found);
        Assert.Equal(created.MessageId, found.MessageId);
        Assert.Equal("Sent", found.Status);
    }

    [Fact]
    public async Task FindBySidAsync_NotExists_ReturnsNull()
    {
        Message? found = await _repo.FindBySidAsync("SM_NONEXISTENT");
        Assert.Null(found);
    }

    // ── UpdateSentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateSentAsync_UpdatesSidAndStatus()
    {
        Message msg = await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "Test",
            null, "general", _userId);

        await _repo.UpdateSentAsync(msg.MessageId, "SM_ABC", "Sent");

        Message? updated = await _repo.FindBySidAsync("SM_ABC");
        Assert.NotNull(updated);
        Assert.Equal("SM_ABC", updated.TwilioSid);
        Assert.Equal("Sent", updated.Status);
    }

    // ── UpdateStatusBySidAsync ───────────────────────────────────────

    [Fact]
    public async Task UpdateStatusBySidAsync_UpdatesStatusAndError()
    {
        Message msg = await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "Test",
            null, "general", _userId);
        await _repo.UpdateSentAsync(msg.MessageId, "SM_STATUS", "Sent");

        await _repo.UpdateStatusBySidAsync("SM_STATUS", "Failed", "30006", "Landline number");

        Message? updated = await _repo.FindBySidAsync("SM_STATUS");
        Assert.NotNull(updated);
        Assert.Equal("Failed", updated.Status);
        Assert.Equal("30006", updated.ErrorCode);
        Assert.Equal("Landline number", updated.ErrorText);
    }

    // ── GetByThreadAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetByThreadAsync_ReturnsMessages_OrderedByCreatedAtDesc()
    {
        await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "First",
            null, "general", _userId);

        await _repo.CreateInboundAsync(
            _storeId, _threadId, "+19294990435",
            "+17185551234", "+19294990435", "Second",
            null, "general");

        List<Message> messages = await _repo.GetByThreadAsync(_storeId, _threadId);

        Assert.Equal(2, messages.Count);
        // Most recent first
        Assert.Equal("Second", messages[0].Body);
        Assert.Equal("First", messages[1].Body);
    }

    [Fact]
    public async Task GetByThreadAsync_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repo.CreateOutboundAsync(
                _storeId, _threadId, "+19294990435",
                "+19294990435", "+17185551234", $"Msg {i}",
                null, "general", _userId);
        }

        List<Message> messages = await _repo.GetByThreadAsync(_storeId, _threadId, limit: 3);
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public async Task GetByThreadAsync_StoreIsolation_OnlyReturnsMatchingStore()
    {
        // Create a message in a different store's thread
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        ThreadEntity thread2 = new ThreadEntity { StoreId = store2.StoreId, Status = "Open" };
        _db.Threads.Add(thread2);
        _db.SaveChanges();

        await _repo.CreateOutboundAsync(
            store2.StoreId, thread2.ThreadId, "+10000000000",
            "+10000000000", "+17185551234", "Other store",
            null, "general", null);

        // Query original store -- should not see the other store's message
        List<Message> messages = await _repo.GetByThreadAsync(_storeId, _threadId);
        Assert.Empty(messages);
    }

    // ── GetLastMessageAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetLastMessageAsync_ReturnsLatest()
    {
        await _repo.CreateOutboundAsync(
            _storeId, _threadId, "+19294990435",
            "+19294990435", "+17185551234", "First",
            null, "general", _userId);

        await _repo.CreateInboundAsync(
            _storeId, _threadId, "+19294990435",
            "+17185551234", "+19294990435", "Latest",
            null, "general");

        Message? last = await _repo.GetLastMessageAsync(_threadId);
        Assert.NotNull(last);
        Assert.Equal("Latest", last.Body);
    }

    [Fact]
    public async Task GetLastMessageAsync_EmptyThread_ReturnsNull()
    {
        Message? last = await _repo.GetLastMessageAsync(_threadId);
        Assert.Null(last);
    }

    // ── CreateNoteAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateNoteAsync_ReloadsInSameThread_WithoutChangingContactPhone()
    {
        Message note = await _repo.CreateNoteAsync(_storeId, _threadId, "Staff note here", _userId);

        _db.ChangeTracker.Clear();
        List<Message> reloaded = await _repo.GetByThreadAsync(_storeId, _threadId);
        Message reloadedNote = Assert.Single(reloaded);
        ThreadEntity thread = await _db.Threads.AsNoTracking().SingleAsync(t => t.ThreadId == _threadId);

        Assert.Equal("Note", note.Direction);
        Assert.Equal("Note", reloadedNote.Direction);
        Assert.Equal("Internal", note.Status);
        Assert.Equal("Staff note here", note.Body);
        Assert.Equal(_userId, note.SentByUserId);
        Assert.Equal("system", note.FromE164);
        Assert.Equal("system", note.ToE164);
        Assert.Equal("+17185550199", thread.ContactPhoneE164);
    }
}
