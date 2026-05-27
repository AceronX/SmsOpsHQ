using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.HubClient;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for the phone-list query that Phase 5 added to
/// <see cref="HeartbeatPusher"/>. The method is internal-static and depends
/// only on an <c>AppDbContext</c>, so we drive it directly against an
/// in-memory SQLite without standing up the rest of the pusher's collaborators.
/// </summary>
public class HeartbeatPusherPhoneListTests : IDisposable
{
    private readonly AppDbContext _db;

    public HeartbeatPusherPhoneListTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private async Task<(int storeId, int defaultNumberId)> SeedStoreWithNumbersAsync(
        params (string Phone, bool IsActive)[] numbers)
    {
        StoreEntity store = new() { StoreName = "S", IsActive = true };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        int? firstNumberId = null;
        foreach ((string phone, bool active) in numbers)
        {
            TwilioNumberEntity n = new()
            {
                StoreId = store.StoreId,
                PhoneE164 = phone,
                IsActive = active,
                FriendlyName = phone,
            };
            _db.TwilioNumbers.Add(n);
            await _db.SaveChangesAsync();
            firstNumberId ??= n.NumberId;
        }

        store.DefaultNumberId = firstNumberId ?? 0;
        await _db.SaveChangesAsync();
        return (store.StoreId, store.DefaultNumberId);
    }

    [Fact]
    public async Task BuildPhoneListAsync_NoNumbers_ReturnsEmptyList()
    {
        StoreEntity store = new() { StoreName = "Empty", IsActive = true };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        List<StorePhoneSnapshot> phones = await HeartbeatPusher.BuildPhoneListAsync(_db, CancellationToken.None);
        Assert.Empty(phones);
    }

    [Fact]
    public async Task BuildPhoneListAsync_ActiveNumbersOnly_AreReturned()
    {
        await SeedStoreWithNumbersAsync(
            ("+15551110001", true),
            ("+15551110002", true),
            ("+15551110003", false));

        List<StorePhoneSnapshot> phones = await HeartbeatPusher.BuildPhoneListAsync(_db, CancellationToken.None);

        Assert.Equal(2, phones.Count);
        Assert.Contains(phones, p => p.PhoneE164 == "+15551110001");
        Assert.Contains(phones, p => p.PhoneE164 == "+15551110002");
        Assert.DoesNotContain(phones, p => p.PhoneE164 == "+15551110003");
    }

    [Fact]
    public async Task BuildPhoneListAsync_FlagsDefaultNumberOnly()
    {
        await SeedStoreWithNumbersAsync(
            ("+15551110001", true),  // first inserted -> becomes default
            ("+15551110002", true),
            ("+15551110003", true));

        List<StorePhoneSnapshot> phones = await HeartbeatPusher.BuildPhoneListAsync(_db, CancellationToken.None);

        StorePhoneSnapshot defaultRow = Assert.Single(phones, p => p.IsDefault);
        Assert.Equal("+15551110001", defaultRow.PhoneE164);
        Assert.Equal(2, phones.Count(p => !p.IsDefault));
    }

    [Fact]
    public async Task BuildPhoneListAsync_MultipleStores_ReturnsAllActiveNumbersAcrossStores()
    {
        // The store API usually hosts one store, but the DB allows many.
        // Each store's default is flagged independently.
        await SeedStoreWithNumbersAsync(("+15551110001", true), ("+15551110002", true));
        await SeedStoreWithNumbersAsync(("+15552220001", true));

        List<StorePhoneSnapshot> phones = await HeartbeatPusher.BuildPhoneListAsync(_db, CancellationToken.None);

        Assert.Equal(3, phones.Count);
        Assert.Equal(2, phones.Count(p => p.IsDefault));
    }

    [Fact]
    public async Task BuildPhoneListAsync_SkipsBlankPhoneStrings()
    {
        StoreEntity store = new() { StoreName = "S", IsActive = true };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _db.TwilioNumbers.Add(new TwilioNumberEntity { StoreId = store.StoreId, PhoneE164 = "+15551112222", IsActive = true });
        _db.TwilioNumbers.Add(new TwilioNumberEntity { StoreId = store.StoreId, PhoneE164 = "   ", IsActive = true });
        _db.TwilioNumbers.Add(new TwilioNumberEntity { StoreId = store.StoreId, PhoneE164 = "", IsActive = true });
        await _db.SaveChangesAsync();

        List<StorePhoneSnapshot> phones = await HeartbeatPusher.BuildPhoneListAsync(_db, CancellationToken.None);

        StorePhoneSnapshot row = Assert.Single(phones);
        Assert.Equal("+15551112222", row.PhoneE164);
    }
}
