using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Api.Controllers;

// Live performance metrics endpoints. No Prometheus, no dashboards. Just truth.
// Ported from Python routes_metrics.py.
[ApiController]
[Authorize]
[Route("api/metrics")]
public sealed class MetricsController : ControllerBase
{
    private readonly XpdConcurrencyLimiter _xpdLimiter;

    // In-memory performance samples for thread loading metrics.
    private static readonly List<ThreadLoadSample> Samples = new();
    private static readonly List<double> ViolationTimestamps = new();
    private static readonly object SamplesLock = new();
    private const int MaxSamples = 1000;

    public MetricsController(XpdConcurrencyLimiter xpdLimiter)
    {
        _xpdLimiter = xpdLimiter;
    }

    // Record a thread loading event. Called internally after each thread load.
    public static void RecordThreadLoad(double durationMs, bool xpdDeferred, bool cacheHit, bool violated)
    {
        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        lock (SamplesLock)
        {
            Samples.Add(new ThreadLoadSample
            {
                Timestamp = now,
                DurationMs = durationMs,
                XpdDeferred = xpdDeferred,
                CacheHit = cacheHit,
                Violated = violated
            });

            if (Samples.Count > MaxSamples)
                Samples.RemoveAt(0);

            if (violated)
                ViolationTimestamps.Add(now);

            // Prune violations older than 5 minutes.
            double cutoff = now - 300;
            while (ViolationTimestamps.Count > 0 && ViolationTimestamps[0] < cutoff)
                ViolationTimestamps.RemoveAt(0);
        }
    }

    // GET /api/metrics/thread-loading -- live thread loading performance metrics.
    [HttpGet("thread-loading")]
    public IActionResult GetThreadLoadingMetrics()
    {
        lock (SamplesLock)
        {
            if (Samples.Count == 0)
            {
                XpdLimiterStats limiterStats = _xpdLimiter.GetStats();
                return Ok(new
                {
                    p50_ms = 0.0,
                    p95_ms = 0.0,
                    violations_last_5m = 0,
                    xpd_deferred_pct = 0.0,
                    cache_hit_rate = 0.0,
                    sample_count = 0,
                    xpd_concurrent_max = limiterStats.MaxConcurrent,
                    xpd_active_now = limiterStats.ActiveQueries
                });
            }

            List<double> durations = Samples.Select(s => s.DurationMs).ToList();
            double p50 = CalculatePercentile(durations, 0.50);
            double p95 = CalculatePercentile(durations, 0.95);

            int deferredCount = Samples.Count(s => s.XpdDeferred);
            double deferredPct = (double)deferredCount / Samples.Count * 100;

            int cacheHits = Samples.Count(s => s.CacheHit);
            double cacheHitRate = (double)cacheHits / Samples.Count;

            XpdLimiterStats stats = _xpdLimiter.GetStats();

            return Ok(new
            {
                p50_ms = Math.Round(p50, 1),
                p95_ms = Math.Round(p95, 1),
                violations_last_5m = ViolationTimestamps.Count,
                xpd_deferred_pct = Math.Round(deferredPct, 1),
                cache_hit_rate = Math.Round(cacheHitRate, 3),
                sample_count = Samples.Count,
                xpd_concurrent_max = stats.MaxConcurrent,
                xpd_active_now = stats.ActiveQueries
            });
        }
    }

    // GET /api/metrics/xpd-limiter -- XPD concurrency limiter metrics.
    [HttpGet("xpd-limiter")]
    public IActionResult GetXpdLimiterMetrics()
    {
        XpdLimiterStats stats = _xpdLimiter.GetStats();

        return Ok(new
        {
            active_count = stats.ActiveQueries,
            max_concurrent = stats.MaxConcurrent,
            total_queries = stats.TotalQueries,
            skipped_queries = stats.SkippedQueries,
            avg_wait_time_ms = Math.Round(stats.AvgWaitTimeMs, 1)
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static double CalculatePercentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0.0;

        List<double> sorted = values.OrderBy(v => v).ToList();
        int index = (int)(sorted.Count * percentile);
        if (index >= sorted.Count)
            index = sorted.Count - 1;

        return sorted[index];
    }

    private sealed class ThreadLoadSample
    {
        public double Timestamp { get; set; }
        public double DurationMs { get; set; }
        public bool XpdDeferred { get; set; }
        public bool CacheHit { get; set; }
        public bool Violated { get; set; }
    }
}
