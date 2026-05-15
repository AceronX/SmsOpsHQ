using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for IdentityResolver: phone-to-CustomerKey resolution via CustomerPhones index and negative cache.
public class IdentityResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IdentityResolver _resolver;

    public IdentityResolverTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        // Seed CustomerPhones: 5559876543 -> keys 100, 200; 5551111111 -> 300
        _db.CustomerPhones.AddRange(
            new CustomerPhoneEntity { CustomerKey = 100, PhoneNormalized = "5559876543", PhoneOriginal = "+15559876543", SourceField = "ResPhone", MatchType = "direct_res_phone", IsDirect = true },
            new CustomerPhoneEntity { CustomerKey = 200, PhoneNormalized = "5559876543", PhoneOriginal = "+15559876543", SourceField = "BusPhone", MatchType = "direct_bus_phone", IsDirect = true },
            new CustomerPhoneEntity { CustomerKey = 300, PhoneNormalized = "5551111111", PhoneOriginal = "+15551111111", SourceField = "ResPhone", MatchType = "direct_res_phone", IsDirect = true }
        );
        _db.SaveChanges();

        _cache = new MemoryCache(new MemoryCacheOptions());
        _resolver = new IdentityResolver(_db, _cache, NullLogger<IdentityResolver>.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_KnownPhone_ReturnsAllKeys()
    {
        List<int> keys = await _resolver.ResolveCustomerKeysAsync("+15559876543");
        Assert.Equal(2, keys.Count);
        Assert.Contains(100, keys);
        Assert.Contains(200, keys);
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_KnownPhone_KeysAreSorted()
    {
        List<int> keys = await _resolver.ResolveCustomerKeysAsync("+15559876543");
        Assert.Equal(new List<int> { 100, 200 }, keys);
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_SingleKey_ReturnsList()
    {
        List<int> keys = await _resolver.ResolveCustomerKeysAsync("+15551111111");
        Assert.Single(keys);
        Assert.Equal(300, keys[0]);
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_UnknownPhone_ReturnsEmpty()
    {
        List<int> keys = await _resolver.ResolveCustomerKeysAsync("+15550000000");
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_InvalidPhone_ReturnsEmpty()
    {
        List<int> keys = await _resolver.ResolveCustomerKeysAsync("abc");
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ResolveCustomerKeysAsync_DifferentFormats_NormalizesCorrectly()
    {
        List<int> keys1 = await _resolver.ResolveCustomerKeysAsync("5559876543");
        List<int> keys2 = await _resolver.ResolveCustomerKeysAsync("+15559876543");
        List<int> keys3 = await _resolver.ResolveCustomerKeysAsync("15559876543");
        Assert.Equal(2, keys1.Count);
        Assert.Equal(2, keys2.Count);
        Assert.Equal(2, keys3.Count);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_SharedPhone_ReturnsMinKey()
    {
        int? identity = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15559876543");
        Assert.NotNull(identity);
        Assert.Equal(100, identity);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_SingleKey_ReturnsThatKey()
    {
        int? identity = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15551111111");
        Assert.NotNull(identity);
        Assert.Equal(300, identity);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_UnknownPhone_ReturnsNull()
    {
        int? identity = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15550000000");
        Assert.Null(identity);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_InvalidPhone_ReturnsNull()
    {
        int? identity = await _resolver.ResolveIdentityIdAsync(storeId: 1, "");
        Assert.Null(identity);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_NegativeCache_SecondCallUsesCachedNull()
    {
        int? first = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15550000000");
        Assert.Null(first);

        _db.CustomerPhones.Add(new CustomerPhoneEntity { CustomerKey = 999, PhoneNormalized = "5550000000", PhoneOriginal = "+15550000000", SourceField = "ResPhone", MatchType = "direct_res_phone", IsDirect = true });
        await _db.SaveChangesAsync();

        int? second = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15550000000");
        Assert.Null(second);
    }

    [Fact]
    public async Task ResolveIdentityIdAsync_KnownPhone_NotNegativelyCached()
    {
        int? first = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15559876543");
        int? second = await _resolver.ResolveIdentityIdAsync(storeId: 1, "+15559876543");
        Assert.Equal(100, first);
        Assert.Equal(100, second);
    }
}
