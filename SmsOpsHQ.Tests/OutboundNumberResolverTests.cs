using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class OutboundNumberResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OutboundNumberResolver _resolver;

    public OutboundNumberResolverTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();
        _resolver = new OutboundNumberResolver(_db);
    }

    [Fact]
    public async Task ResolveAsync_SelectedActiveNumber_ReturnsExactNumber()
    {
        (StoreEntity store, TwilioNumberEntity number) = await SeedStoreWithNumberAsync(
            "+15551110001", active: true, makeDefault: false);

        OutboundNumberResolution result = await _resolver.ResolveAsync(store.StoreId, number.NumberId);

        Assert.Equal(number.NumberId, result.TwilioNumberId);
        Assert.Equal("+15551110001", result.PhoneE164);
    }

    [Fact]
    public async Task ResolveAsync_NoSelection_UsesStoreDefault()
    {
        (StoreEntity store, TwilioNumberEntity number) = await SeedStoreWithNumberAsync(
            "+15551110002", active: true, makeDefault: true);

        OutboundNumberResolution result = await _resolver.ResolveAsync(store.StoreId, null);

        Assert.Equal(number.NumberId, result.TwilioNumberId);
        Assert.Equal("+15551110002", result.PhoneE164);
    }

    [Fact]
    public async Task ResolveAsync_CrossStoreSelection_ThrowsWithoutFallback()
    {
        (StoreEntity store1, _) = await SeedStoreWithNumberAsync(
            "+15551110003", active: true, makeDefault: true);
        (_, TwilioNumberEntity store2Number) = await SeedStoreWithNumberAsync(
            "+15551110004", active: true, makeDefault: true);

        OutboundNumberValidationException ex = await Assert.ThrowsAsync<OutboundNumberValidationException>(
            () => _resolver.ResolveAsync(store1.StoreId, store2Number.NumberId));

        Assert.Contains("does not belong", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_InactiveSelection_ThrowsWithoutFallback()
    {
        (StoreEntity store, TwilioNumberEntity inactive) = await SeedStoreWithNumberAsync(
            "+15551110005", active: false, makeDefault: false);

        OutboundNumberValidationException ex = await Assert.ThrowsAsync<OutboundNumberValidationException>(
            () => _resolver.ResolveAsync(store.StoreId, inactive.NumberId));

        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_MissingDefault_Throws()
    {
        StoreEntity store = new() { StoreName = "No Number", IsActive = true };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        OutboundNumberValidationException ex = await Assert.ThrowsAsync<OutboundNumberValidationException>(
            () => _resolver.ResolveAsync(store.StoreId, null));

        Assert.Contains("No default", ex.Message);
    }

    private async Task<(StoreEntity Store, TwilioNumberEntity Number)> SeedStoreWithNumberAsync(
        string phone,
        bool active,
        bool makeDefault)
    {
        StoreEntity store = new() { StoreName = $"Store {phone[^4..]}", IsActive = true };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        TwilioNumberEntity number = new()
        {
            StoreId = store.StoreId,
            PhoneE164 = phone,
            IsActive = active
        };
        _db.TwilioNumbers.Add(number);
        await _db.SaveChangesAsync();

        if (makeDefault)
        {
            store.DefaultNumberId = number.NumberId;
            await _db.SaveChangesAsync();
        }

        return (store, number);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
