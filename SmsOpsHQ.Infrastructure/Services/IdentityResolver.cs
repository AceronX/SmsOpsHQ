using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Models;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Services;

// Resolves phone numbers to pawn customer identities via the CustomerPhones index (fast indexed lookup).
// Includes a negative cache (10 min TTL) for phones not found.
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

    // Normalize the phone and query CustomerPhones by PhoneNormalized (indexed — fast).
    public async Task<List<int>> ResolveCustomerKeysAsync(string phoneE164,
        CancellationToken cancellationToken = default)
    {
        List<CustomerPhoneMatch> matches = await ResolveCustomerPhoneMatchesAsync(phoneE164, cancellationToken);
        return matches.Select(m => m.CustomerKey).Distinct().OrderBy(k => k).ToList();
    }

    public async Task<List<CustomerPhoneMatch>> ResolveCustomerPhoneMatchesAsync(string phoneE164,
        CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phoneE164);
        if (normalized is null)
            return new List<CustomerPhoneMatch>();

        List<CustomerPhoneEntity> rows = await _db.CustomerPhones
            .AsNoTracking()
            .Where(p => p.PhoneNormalized == normalized)
            .ToListAsync(cancellationToken);

        var list = new List<CustomerPhoneMatch>(rows.Count);
        foreach (CustomerPhoneEntity p in rows)
        {
            string source = p.SourceField ?? "";
            int rank = source switch
            {
                "ResPhone" => 100,
                "BusPhone" => 90,
                "Notes" => 30,
                "TicketNotes" => 25,
                _ => 10
            };

            string matchType = string.IsNullOrEmpty(p.MatchType)
                ? InferMatchType(source, p.IsDirect)
                : p.MatchType!;

            bool isDirect = p.IsDirect || source is "ResPhone" or "BusPhone";

            list.Add(new CustomerPhoneMatch
            {
                CustomerKey = p.CustomerKey,
                PhoneNormalized = p.PhoneNormalized,
                SourceField = source,
                MatchType = matchType,
                IsDirect = isDirect,
                MatchRank = rank
            });
        }

        return list
            .GroupBy(x => new { x.CustomerKey, x.PhoneNormalized, x.SourceField, x.MatchType, x.IsDirect })
            .Select(g => g.First())
            .OrderByDescending(x => x.MatchRank)
            .ThenBy(x => x.CustomerKey)
            .ToList();
    }

    private static string InferMatchType(string sourceField, bool isDirect)
    {
        if (isDirect || sourceField is "ResPhone" or "BusPhone")
            return sourceField == "BusPhone" ? "direct_bus_phone" : "direct_res_phone";

        return sourceField == "TicketNotes" ? "ticket_note_reference" : "note_reference";
    }

    // Resolve a phone number to a single canonical identity ID.
    // Returns MIN(CustomerKey) from the resolved keys, or null if not found.
    public async Task<int?> ResolveIdentityIdAsync(int storeId, string phoneE164,
        CancellationToken cancellationToken = default)
    {
        string? normalized = PhoneUtils.ExtractLast10Digits(phoneE164);
        if (normalized is null)
            return null;

        string cacheKey = NegativeCachePrefix + normalized;

        if (_cache.TryGetValue(cacheKey, out _))
        {
            _logger.LogDebug("Identity negative cache hit for {Phone}", normalized);
            return null;
        }

        List<int> keys = await ResolveCustomerKeysAsync(phoneE164, cancellationToken);

        if (keys.Count == 0)
        {
            _cache.Set(cacheKey, true, NegativeCacheTtl);
            _logger.LogDebug("Identity not found for {Phone}, cached negative result", normalized);
            return null;
        }

        int minKey = keys.Min();
        _logger.LogDebug("Identity resolved for {Phone}: CustomerKey={Key} (from {Count} keys)",
            normalized, minKey, keys.Count);

        return minKey;
    }
}
