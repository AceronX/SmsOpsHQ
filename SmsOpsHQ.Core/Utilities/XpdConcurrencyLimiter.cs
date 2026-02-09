namespace SmsOpsHQ.Core.Utilities;

// Non-blocking semaphore for XPD SQLite queries with fast-fail semantics.
//
// Thread rendering must NEVER wait on XPD availability. XPD is opportunistic
// enrichment, not a blocking dependency. This limiter uses NON-BLOCKING
// acquisition to prevent thread endpoint stalls.
//
// Performance Targets:
//  - Semaphore acquisition: < 50ms or skip
//  - No blocking waits that couple thread latency to XPD contention
//  - Skip rate monitoring for capacity planning
public sealed class XpdConcurrencyLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrent;

    private int _activeQueries;
    private long _totalQueries;
    private long _skippedQueries;
    private double _totalWaitTimeMs;
    private readonly object _statsLock = new();

    public XpdConcurrencyLimiter(int maxConcurrent = 2)
    {
        _maxConcurrent = maxConcurrent;
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    // Try to acquire a semaphore slot with minimal timeout.
    // Default timeout is 50ms. Anything longer risks coupling
    // thread latency to XPD contention.
    // Returns true if acquired, false if unavailable.
    public bool TryAcquire(TimeSpan? timeout = null)
    {
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(50);
        long startTicks = Environment.TickCount64;

        bool acquired = _semaphore.Wait(effectiveTimeout);
        double waitMs = Environment.TickCount64 - startTicks;

        lock (_statsLock)
        {
            if (acquired)
            {
                Interlocked.Increment(ref _activeQueries);
                Interlocked.Increment(ref _totalQueries);
                _totalWaitTimeMs += waitMs;
            }
            else
            {
                Interlocked.Increment(ref _skippedQueries);
            }
        }

        return acquired;
    }

    // Async version of TryAcquire.
    public async Task<bool> TryAcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(50);
        long startTicks = Environment.TickCount64;

        bool acquired = await _semaphore.WaitAsync(effectiveTimeout, cancellationToken);
        double waitMs = Environment.TickCount64 - startTicks;

        lock (_statsLock)
        {
            if (acquired)
            {
                Interlocked.Increment(ref _activeQueries);
                Interlocked.Increment(ref _totalQueries);
                _totalWaitTimeMs += waitMs;
            }
            else
            {
                Interlocked.Increment(ref _skippedQueries);
            }
        }

        return acquired;
    }

    // Release a semaphore slot after XPD query completes.
    public void Release()
    {
        Interlocked.Decrement(ref _activeQueries);
        _semaphore.Release();
    }

    // Execute an action opportunistically. If the semaphore cannot be acquired
    // within the timeout, the action is skipped and the fallback value is returned.
    public T Opportunistic<T>(Func<T> action, T fallbackValue, TimeSpan? timeout = null)
    {
        if (!TryAcquire(timeout))
            return fallbackValue;

        try
        {
            return action();
        }
        finally
        {
            Release();
        }
    }

    // Async version of Opportunistic.
    public async Task<T> OpportunisticAsync<T>(
        Func<Task<T>> action,
        T fallbackValue,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!await TryAcquireAsync(timeout, cancellationToken))
            return fallbackValue;

        try
        {
            return await action();
        }
        finally
        {
            Release();
        }
    }

    // Get limiter statistics for monitoring and capacity planning.
    // High skip rates indicate XPD is under-provisioned or queries are too slow.
    public XpdLimiterStats GetStats()
    {
        lock (_statsLock)
        {
            long totalAttempts = _totalQueries + _skippedQueries;
            double skipRate = totalAttempts > 0
                ? (double)_skippedQueries / totalAttempts * 100
                : 0;

            double avgWaitMs = _totalQueries > 0
                ? _totalWaitTimeMs / _totalQueries
                : 0;

            return new XpdLimiterStats
            {
                MaxConcurrent = _maxConcurrent,
                ActiveQueries = _activeQueries,
                TotalQueries = _totalQueries,
                SkippedQueries = _skippedQueries,
                SkipRatePercent = Math.Round(skipRate, 1),
                AvgWaitTimeMs = Math.Round(avgWaitMs, 2)
            };
        }
    }

    // Reset statistics counters. Useful for testing or periodic resets.
    public void ResetStats()
    {
        lock (_statsLock)
        {
            _totalQueries = 0;
            _skippedQueries = 0;
            _totalWaitTimeMs = 0;
        }
    }
}

// Statistics snapshot from the XPD concurrency limiter.
public sealed class XpdLimiterStats
{
    public int MaxConcurrent { get; set; }
    public int ActiveQueries { get; set; }
    public long TotalQueries { get; set; }
    public long SkippedQueries { get; set; }
    public double SkipRatePercent { get; set; }
    public double AvgWaitTimeMs { get; set; }
}
