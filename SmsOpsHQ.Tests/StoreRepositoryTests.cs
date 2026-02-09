using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for StoreRepository against an in-memory SQLite database.
public class StoreRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StoreRepository _repo;
    private readonly int _storeId;
    private readonly int _numberId;

    public StoreRepositoryTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        // Seed a store with a Twilio number.
        StoreEntity store = new StoreEntity
        {
            StoreName = "Pitkin Ave",
            Address = "100 Pitkin Ave",
            City = "Brooklyn",
            State = "NY",
            Zip = "11207",
            Phone = "7185551234",
            IsActive = true
        };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity number = new TwilioNumberEntity
        {
            StoreId = _storeId,
            PhoneE164 = "+19294990435",
            FriendlyName = "Pitkin Main",
            IsActive = true
        };
        _db.TwilioNumbers.Add(number);
        _db.SaveChanges();
        _numberId = number.NumberId;

        // Set default number on store
        store.DefaultNumberId = _numberId;
        _db.SaveChanges();

        _repo = new StoreRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingStore_ReturnsMapped()
    {
        Store? store = await _repo.GetByIdAsync(_storeId);

        Assert.NotNull(store);
        Assert.Equal("Pitkin Ave", store.StoreName);
        Assert.Equal("Brooklyn", store.City);
        Assert.Equal("NY", store.State);
        Assert.Equal(_numberId, store.DefaultNumberId);
    }

    [Fact]
    public async Task GetByIdAsync_Nonexistent_ReturnsNull()
    {
        Store? store = await _repo.GetByIdAsync(99999);
        Assert.Null(store);
    }

    // ── GetByPhoneAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByPhoneAsync_KnownNumber_ReturnsStore()
    {
        Store? store = await _repo.GetByPhoneAsync("+19294990435");

        Assert.NotNull(store);
        Assert.Equal(_storeId, store.StoreId);
        Assert.Equal("Pitkin Ave", store.StoreName);
    }

    [Fact]
    public async Task GetByPhoneAsync_UnknownNumber_ReturnsNull()
    {
        Store? store = await _repo.GetByPhoneAsync("+10000000000");
        Assert.Null(store);
    }

    [Fact]
    public async Task GetByPhoneAsync_InactiveNumber_ReturnsNull()
    {
        // Deactivate the number
        TwilioNumberEntity? num = await _db.TwilioNumbers.FindAsync(_numberId);
        num!.IsActive = false;
        await _db.SaveChangesAsync();

        Store? store = await _repo.GetByPhoneAsync("+19294990435");
        Assert.Null(store);
    }

    // ── GetDefaultNumberAsync ────────────────────────────────────────

    [Fact]
    public async Task GetDefaultNumberAsync_WithDefault_ReturnsPhoneE164()
    {
        string? phone = await _repo.GetDefaultNumberAsync(_storeId);

        Assert.NotNull(phone);
        Assert.Equal("+19294990435", phone);
    }

    [Fact]
    public async Task GetDefaultNumberAsync_NoDefault_ReturnsNull()
    {
        // Create a store without default number
        StoreEntity store2 = new StoreEntity
        {
            StoreName = "No Default Store",
            IsActive = true,
            DefaultNumberId = null
        };
        _db.Stores.Add(store2);
        _db.SaveChanges();

        string? phone = await _repo.GetDefaultNumberAsync(store2.StoreId);
        Assert.Null(phone);
    }

    [Fact]
    public async Task GetDefaultNumberAsync_NonexistentStore_ReturnsNull()
    {
        string? phone = await _repo.GetDefaultNumberAsync(99999);
        Assert.Null(phone);
    }
}
