using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for OptOutRepository against an in-memory SQLite database.
public class OptOutRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OptOutRepository _repo;
    private readonly int _storeId;

    public OptOutRepositoryTests()
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

        _repo = new OptOutRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── AddAsync / ExistsAsync ───────────────────────────────────────

    [Fact]
    public async Task AddAsync_CreatesOptOut()
    {
        await _repo.AddAsync(_storeId, "+17185551234", "Customer requested");

        bool exists = await _repo.ExistsAsync(_storeId, "+17185551234");
        Assert.True(exists);
    }

    [Fact]
    public async Task AddAsync_Idempotent_DoesNotDuplicate()
    {
        await _repo.AddAsync(_storeId, "+17185551234", "First");
        await _repo.AddAsync(_storeId, "+17185551234", "Second");

        List<OptOut> all = await _repo.GetAllAsync(_storeId);
        Assert.Single(all);
        Assert.Equal("First", all[0].Reason); // Original kept
    }

    // ── ExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_NotOptedOut_ReturnsFalse()
    {
        bool exists = await _repo.ExistsAsync(_storeId, "+10000000000");
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_StoreIsolation()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        await _repo.AddAsync(_storeId, "+17185551234", null);

        // Same phone, different store
        bool existsStore1 = await _repo.ExistsAsync(_storeId, "+17185551234");
        bool existsStore2 = await _repo.ExistsAsync(store2.StoreId, "+17185551234");

        Assert.True(existsStore1);
        Assert.False(existsStore2);
    }

    // ── GetAllAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllForStore()
    {
        await _repo.AddAsync(_storeId, "+11111111111", null);
        await _repo.AddAsync(_storeId, "+12222222222", "Spam");

        List<OptOut> all = await _repo.GetAllAsync(_storeId);

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetAllAsync_Empty_ReturnsEmptyList()
    {
        List<OptOut> all = await _repo.GetAllAsync(_storeId);
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetAllAsync_StoreIsolation()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        await _repo.AddAsync(_storeId, "+11111111111", null);
        await _repo.AddAsync(store2.StoreId, "+12222222222", null);

        List<OptOut> store1 = await _repo.GetAllAsync(_storeId);
        Assert.Single(store1);
    }

    // ── RemoveAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_RemovesOptOut()
    {
        await _repo.AddAsync(_storeId, "+17185551234", null);
        Assert.True(await _repo.ExistsAsync(_storeId, "+17185551234"));

        await _repo.RemoveAsync(_storeId, "+17185551234");

        Assert.False(await _repo.ExistsAsync(_storeId, "+17185551234"));
    }

    [Fact]
    public async Task RemoveAsync_Nonexistent_DoesNotThrow()
    {
        await _repo.RemoveAsync(_storeId, "+10000000000");
    }

    [Fact]
    public async Task RemoveAsync_StoreIsolation_OnlyRemovesFromCorrectStore()
    {
        StoreEntity store2 = new StoreEntity { StoreName = "Store 2", IsActive = true };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        await _repo.AddAsync(_storeId, "+17185551234", null);
        await _repo.AddAsync(store2.StoreId, "+17185551234", null);

        await _repo.RemoveAsync(_storeId, "+17185551234");

        Assert.False(await _repo.ExistsAsync(_storeId, "+17185551234"));
        Assert.True(await _repo.ExistsAsync(store2.StoreId, "+17185551234"));
    }

    // ── Full lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_AddCheckRemoveCheck()
    {
        Assert.False(await _repo.ExistsAsync(_storeId, "+17185551234"));

        await _repo.AddAsync(_storeId, "+17185551234", "STOP received");
        Assert.True(await _repo.ExistsAsync(_storeId, "+17185551234"));

        List<OptOut> all = await _repo.GetAllAsync(_storeId);
        Assert.Single(all);
        Assert.Equal("STOP received", all[0].Reason);

        await _repo.RemoveAsync(_storeId, "+17185551234");
        Assert.False(await _repo.ExistsAsync(_storeId, "+17185551234"));
    }
}
