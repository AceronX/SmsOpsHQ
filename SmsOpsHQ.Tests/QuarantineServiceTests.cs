using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for QuarantineService: quarantine, list, and resolve messages.
public class QuarantineServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly QuarantineService _service;
    private readonly int _storeId;
    private readonly int _userId;

    public QuarantineServiceTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        // Seed store.
        StoreEntity store = new StoreEntity
        {
            StoreName = "Quarantine Test Store",
            IsActive = true
        };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        // Seed user for review.
        UserEntity user = new UserEntity
        {
            Username = "admin",
            PasswordHash = "hash",
            Role = "HQ",
            StoreId = null,
            TwilioNumberId = null,
            IsActive = true
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _userId = user.UserId;

        _service = new QuarantineService(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task QuarantineMessageAsync_CreatesRecord_ReturnsId()
    {
        int id = await _service.QuarantineMessageAsync(
            _storeId, "+15559876543", "+15551234567",
            "Suspicious message", null, "SM_abc123", "Metadata mismatch");

        Assert.True(id > 0);

        // Verify in DB.
        QuarantinedMessageEntity? entity = await _db.QuarantinedMessages
            .FirstOrDefaultAsync(q => q.QuarantineId == id);
        Assert.NotNull(entity);
        Assert.Equal(_storeId, entity.StoreId);
        Assert.Equal("+15559876543", entity.FromE164);
        Assert.Equal("+15551234567", entity.ToE164);
        Assert.Equal("Suspicious message", entity.Body);
        Assert.Equal("SM_abc123", entity.TwilioSid);
        Assert.Equal("Metadata mismatch", entity.QuarantineReason);
        Assert.Null(entity.Resolution);
        Assert.Null(entity.ReviewedAt);
    }

    [Fact]
    public async Task QuarantineMessageAsync_WithMedia_CreatesRecord()
    {
        int id = await _service.QuarantineMessageAsync(
            _storeId, "+15559876543", "+15551234567",
            null, "[\"https://example.com/img.jpg\"]", null, "Spam");

        QuarantinedMessageEntity? entity = await _db.QuarantinedMessages
            .FirstOrDefaultAsync(q => q.QuarantineId == id);
        Assert.NotNull(entity);
        Assert.Null(entity.Body);
        Assert.Equal("[\"https://example.com/img.jpg\"]", entity.MediaJson);
    }

    [Fact]
    public async Task GetMessagesAsync_Unresolved_ReturnsOnlyPending()
    {
        await _service.QuarantineMessageAsync(
            _storeId, "+15551111111", "+15551234567", "Msg1", null, null, "Reason1");
        int resolvedId = await _service.QuarantineMessageAsync(
            _storeId, "+15552222222", "+15551234567", "Msg2", null, null, "Reason2");

        // Resolve one.
        await _service.ResolveAsync(resolvedId, "Approved", _userId);

        // Default: unresolved only.
        List<QuarantinedMessage> pending = await _service.GetMessagesAsync();
        Assert.Single(pending);
        Assert.Equal("Msg1", pending[0].Body);
    }

    [Fact]
    public async Task GetMessagesAsync_FilterByResolution_ReturnsMatching()
    {
        await _service.QuarantineMessageAsync(
            _storeId, "+15551111111", "+15551234567", "Msg1", null, null, "Reason");
        int approvedId = await _service.QuarantineMessageAsync(
            _storeId, "+15552222222", "+15551234567", "Msg2", null, null, "Reason");

        await _service.ResolveAsync(approvedId, "Approved", _userId);

        List<QuarantinedMessage> approved = await _service.GetMessagesAsync(
            resolution: "Approved");
        Assert.Single(approved);
        Assert.Equal("Msg2", approved[0].Body);
    }

    [Fact]
    public async Task GetMessagesAsync_OrderedByQuarantinedAtDesc()
    {
        await _service.QuarantineMessageAsync(
            _storeId, "+15551111111", "+15551234567", "First", null, null, "Reason");
        await Task.Delay(10); // Ensure different timestamps.
        await _service.QuarantineMessageAsync(
            _storeId, "+15552222222", "+15551234567", "Second", null, null, "Reason");

        List<QuarantinedMessage> messages = await _service.GetMessagesAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Second", messages[0].Body);
        Assert.Equal("First", messages[1].Body);
    }

    [Fact]
    public async Task GetMessagesAsync_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
        {
            await _service.QuarantineMessageAsync(
                _storeId, $"+1555000000{i}", "+15551234567", $"Msg{i}", null, null, "Reason");
        }

        List<QuarantinedMessage> messages = await _service.GetMessagesAsync(limit: 2);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task GetMessagesAsync_Empty_ReturnsEmptyList()
    {
        List<QuarantinedMessage> messages = await _service.GetMessagesAsync();
        Assert.Empty(messages);
    }

    [Fact]
    public async Task ResolveAsync_ExistingRecord_UpdatesResolution()
    {
        int id = await _service.QuarantineMessageAsync(
            _storeId, "+15559876543", "+15551234567", "Test", null, null, "Reason");

        bool result = await _service.ResolveAsync(id, "Rejected", _userId);

        Assert.True(result);

        QuarantinedMessageEntity? entity = await _db.QuarantinedMessages
            .FirstOrDefaultAsync(q => q.QuarantineId == id);
        Assert.NotNull(entity);
        Assert.Equal("Rejected", entity.Resolution);
        Assert.NotNull(entity.ReviewedAt);
        Assert.Equal(_userId, entity.ReviewedByUserId);
    }

    [Fact]
    public async Task ResolveAsync_Nonexistent_ReturnsFalse()
    {
        bool result = await _service.ResolveAsync(9999, "Approved", _userId);

        Assert.False(result);
    }

    [Fact]
    public async Task ResolveAsync_NullUserId_StillResolves()
    {
        int id = await _service.QuarantineMessageAsync(
            _storeId, "+15559876543", "+15551234567", "Test", null, null, "Reason");

        bool result = await _service.ResolveAsync(id, "Spam", null);

        Assert.True(result);

        QuarantinedMessageEntity? entity = await _db.QuarantinedMessages
            .FirstOrDefaultAsync(q => q.QuarantineId == id);
        Assert.NotNull(entity);
        Assert.Equal("Spam", entity.Resolution);
        Assert.Null(entity.ReviewedByUserId);
    }

    [Fact]
    public async Task FullLifecycle_QuarantineListResolveList()
    {
        // Quarantine.
        int id = await _service.QuarantineMessageAsync(
            _storeId, "+15559876543", "+15551234567", "Flagged", null, "SM_test", "Test reason");

        // List (should be pending).
        List<QuarantinedMessage> pending = await _service.GetMessagesAsync();
        Assert.Single(pending);
        Assert.Equal(id, pending[0].QuarantineId);
        Assert.Null(pending[0].Resolution);

        // Resolve.
        bool resolved = await _service.ResolveAsync(id, "Approved", _userId);
        Assert.True(resolved);

        // List pending again (should be empty now).
        List<QuarantinedMessage> afterResolve = await _service.GetMessagesAsync();
        Assert.Empty(afterResolve);

        // List approved.
        List<QuarantinedMessage> approved = await _service.GetMessagesAsync(resolution: "Approved");
        Assert.Single(approved);
        Assert.Equal(id, approved[0].QuarantineId);
    }
}
