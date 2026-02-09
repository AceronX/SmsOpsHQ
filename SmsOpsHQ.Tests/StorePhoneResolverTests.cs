using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for StorePhoneResolver (delegates to StoreRepository).
public class StorePhoneResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StorePhoneResolver _resolver;
    private readonly int _storeId;

    public StorePhoneResolverTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        // Seed store with a Twilio number.
        StoreEntity store = new StoreEntity
        {
            StoreName = "Resolver Test Store",
            IsActive = true
        };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity number = new TwilioNumberEntity
        {
            StoreId = _storeId,
            PhoneE164 = "+15551234567",
            FriendlyName = "Main",
            IsActive = true
        };
        _db.TwilioNumbers.Add(number);
        _db.SaveChanges();

        // Set default number.
        store.DefaultNumberId = number.NumberId;
        _db.SaveChanges();

        StoreRepository storeRepo = new StoreRepository(_db);
        _resolver = new StorePhoneResolver(storeRepo);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task GetStoreByPhoneAsync_KnownNumber_ReturnsStore()
    {
        Store? store = await _resolver.GetStoreByPhoneAsync("+15551234567");

        Assert.NotNull(store);
        Assert.Equal(_storeId, store.StoreId);
    }

    [Fact]
    public async Task GetStoreByPhoneAsync_UnknownNumber_ReturnsNull()
    {
        Store? store = await _resolver.GetStoreByPhoneAsync("+15550000000");

        Assert.Null(store);
    }

    [Fact]
    public async Task GetStorePhoneAsync_WithDefault_ReturnsE164()
    {
        string? phone = await _resolver.GetStorePhoneAsync(_storeId);

        Assert.Equal("+15551234567", phone);
    }

    [Fact]
    public async Task GetStorePhoneAsync_NonexistentStore_ReturnsNull()
    {
        string? phone = await _resolver.GetStorePhoneAsync(9999);

        Assert.Null(phone);
    }
}
