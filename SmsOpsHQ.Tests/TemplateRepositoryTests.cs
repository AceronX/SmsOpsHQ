using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for TemplateRepository against an in-memory SQLite database.
public class TemplateRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TemplateRepository _repo;
    private readonly int _storeId;
    private readonly int _userId;

    public TemplateRepositoryTests()
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

        UserEntity user = new UserEntity
        {
            Username = "test",
            PasswordHash = "hash",
            Role = "StoreAdmin",
            StoreId = _storeId,
            TwilioNumberId = null,
            IsActive = true
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _userId = user.UserId;

        _repo = new TemplateRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── CreateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CreatesTemplate()
    {
        Template template = await _repo.CreateAsync(_storeId, "Greeting", "Hello!", "F1", _userId);

        Assert.True(template.TemplateId > 0);
        Assert.Equal("Greeting", template.Name);
        Assert.Equal("Hello!", template.Body);
        Assert.Equal("F1", template.Hotkey);
        Assert.Equal(_userId, template.CreatedByUserId);
    }

    // ── GetByStoreAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByStoreAsync_ReturnsAllForStore_SortedByName()
    {
        await _repo.CreateAsync(_storeId, "Zebra", "Z", null, null);
        await _repo.CreateAsync(_storeId, "Apple", "A", null, null);
        await _repo.CreateAsync(_storeId, "Middle", "M", null, null);

        List<Template> templates = await _repo.GetByStoreAsync(_storeId);

        Assert.Equal(3, templates.Count);
        Assert.Equal("Apple", templates[0].Name);
        Assert.Equal("Middle", templates[1].Name);
        Assert.Equal("Zebra", templates[2].Name);
    }

    [Fact]
    public async Task GetByStoreAsync_StoreIsolation()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        await _repo.CreateAsync(_storeId, "T1", "Body1", null, null);
        await _repo.CreateAsync(store2.StoreId, "T2", "Body2", null, null);

        List<Template> templates = await _repo.GetByStoreAsync(_storeId);
        Assert.Single(templates);
        Assert.Equal("T1", templates[0].Name);
    }

    [Fact]
    public async Task GetByStoreAsync_Empty_ReturnsEmptyList()
    {
        List<Template> templates = await _repo.GetByStoreAsync(_storeId);
        Assert.Empty(templates);
    }

    // ── UpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        Template created = await _repo.CreateAsync(_storeId, "Old", "Old body", "F1", _userId);

        await _repo.UpdateAsync(created.TemplateId, "New", "New body", "F2");

        List<Template> templates = await _repo.GetByStoreAsync(_storeId);
        Assert.Single(templates);
        Assert.Equal("New", templates[0].Name);
        Assert.Equal("New body", templates[0].Body);
        Assert.Equal("F2", templates[0].Hotkey);
    }

    [Fact]
    public async Task UpdateAsync_Nonexistent_DoesNotThrow()
    {
        await _repo.UpdateAsync(99999, "Name", "Body", null);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesTemplate()
    {
        Template created = await _repo.CreateAsync(_storeId, "ToDelete", "Body", null, null);

        await _repo.DeleteAsync(created.TemplateId);

        List<Template> templates = await _repo.GetByStoreAsync(_storeId);
        Assert.Empty(templates);
    }

    [Fact]
    public async Task DeleteAsync_Nonexistent_DoesNotThrow()
    {
        await _repo.DeleteAsync(99999);
    }

    // ── Full CRUD cycle ──────────────────────────────────────────────

    [Fact]
    public async Task FullCrudCycle_Works()
    {
        // Create
        Template created = await _repo.CreateAsync(_storeId, "Test", "Body", "F5", _userId);
        Assert.True(created.TemplateId > 0);

        // Read
        List<Template> all = await _repo.GetByStoreAsync(_storeId);
        Assert.Single(all);

        // Update
        await _repo.UpdateAsync(created.TemplateId, "Updated", "Updated body", null);
        List<Template> afterUpdate = await _repo.GetByStoreAsync(_storeId);
        Assert.Equal("Updated", afterUpdate[0].Name);
        Assert.Null(afterUpdate[0].Hotkey);

        // Delete
        await _repo.DeleteAsync(created.TemplateId);
        List<Template> afterDelete = await _repo.GetByStoreAsync(_storeId);
        Assert.Empty(afterDelete);
    }
}
