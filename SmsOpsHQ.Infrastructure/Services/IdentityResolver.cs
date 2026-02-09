using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Infrastructure.Services;

// Resolves phone numbers to XPD customer identities via the
// XPD_CustomerPhones index table. Includes a negative cache
// (10 minute TTL) for phones not found.
public sealed class IdentityResolver : IIdentityResolver
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IdentityResolver> _logger;

    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(10);
    private const string NegativeCachePrefix = "identity_neg:";

    public IdentityResolver(AppDbContext db, IMemoryCache cache, ILogger<IdentityResolver> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    // Normalize the phone and query XPD_CustomerPhones by PhoneNormalized.
    // Returns all matching CustomerKeys.
    public async Task<List<int>> ResolveCustomerKeysAsync(string phoneE164,
        CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phoneE164);
        if (normalized is null)
            return new List<int>();

        List<int> keys = await _db.XpdCustomerPhones
            .AsNoTracking()
            .Where(p => p.PhoneNormalized == normalized)
            .Select(p => p.CustomerKey)
            .Distinct()
            .OrderBy(k => k)
            .ToListAsync(cancellationToken);

        return keys;
    }

    // Resolve a phone number to a single canonical identity ID.
    // Returns MIN(CustomerKey) from the resolved keys, or null if not found.
    // Uses a negative cache to avoid repeated lookups for unknown phones.
    public async Task<int?> ResolveIdentityIdAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phoneE164);
        if (normalized is null)
            return null;

        string cacheKey = NegativeCachePrefix + normalized;

        // Check negative cache -- if this phone was recently not found, skip the query
        if (_cache.TryGetValue(cacheKey, out _))
        {
            _logger.LogDebug("Identity negative cache hit for {Phone}", normalized);
            return null;
        }

        List<int> keys = await ResolveCustomerKeysAsync(phoneE164, cancellationToken);

        if (keys.Count == 0)
        {
            // Cache the miss for 10 minutes to avoid hammering the DB
            _cache.Set(cacheKey, true, NegativeCacheTtl);
            _logger.LogDebug("Identity not found for {Phone}, cached negative result", normalized);
            return null;
        }

        // Return the minimum key as the canonical identity
        int minKey = keys.Min();
        _logger.LogDebug("Identity resolved for {Phone}: CustomerKey={Key} (from {Count} keys)",
            normalized, minKey, keys.Count);

        return minKey;
    }
}
