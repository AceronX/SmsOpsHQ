using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class XpdConcurrencyLimiterTests
{
    // =================================================================
    // TryAcquire
    // =================================================================

    [Fact]
    public void TryAcquire_FirstSlot_Succeeds()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        bool acquired = limiter.TryAcquire();
        Assert.True(acquired);
        limiter.Release();
    }

    [Fact]
    public void TryAcquire_AllSlots_Succeeds()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        bool first = limiter.TryAcquire();
        bool second = limiter.TryAcquire();
        Assert.True(first);
        Assert.True(second);
        limiter.Release();
        limiter.Release();
    }

    [Fact]
    public void TryAcquire_BeyondMax_FailsFast()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = limiter.TryAcquire();
        Assert.True(first);

        // Second attempt should fail (no slots available, 10ms timeout)
        bool second = limiter.TryAcquire(timeout: TimeSpan.FromMilliseconds(10));
        Assert.False(second);

        limiter.Release();
    }

    [Fact]
    public void TryAcquire_AfterRelease_SucceedsAgain()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = limiter.TryAcquire();
        Assert.True(first);
        limiter.Release();

        bool second = limiter.TryAcquire();
        Assert.True(second);
        limiter.Release();
    }

    // =================================================================
    // TryAcquireAsync
    // =================================================================

    [Fact]
    public async Task TryAcquireAsync_FirstSlot_Succeeds()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        bool acquired = await limiter.TryAcquireAsync();
        Assert.True(acquired);
        limiter.Release();
    }

    [Fact]
    public async Task TryAcquireAsync_BeyondMax_FailsFast()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = await limiter.TryAcquireAsync();
        Assert.True(first);

        bool second = await limiter.TryAcquireAsync(timeout: TimeSpan.FromMilliseconds(10));
        Assert.False(second);

        limiter.Release();
    }

    // =================================================================
    // Opportunistic
    // =================================================================

    [Fact]
    public void Opportunistic_SlotAvailable_RunsAction()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        int result = limiter.Opportunistic(() => 42, fallbackValue: -1);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Opportunistic_NoSlotAvailable_ReturnsFallback()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = limiter.TryAcquire();
        Assert.True(first);

        int result = limiter.Opportunistic(
            () => 42,
            fallbackValue: -1,
            timeout: TimeSpan.FromMilliseconds(10));

        Assert.Equal(-1, result);
        limiter.Release();
    }

    // =================================================================
    // OpportunisticAsync
    // =================================================================

    [Fact]
    public async Task OpportunisticAsync_SlotAvailable_RunsAction()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        int result = await limiter.OpportunisticAsync(
            () => Task.FromResult(42),
            fallbackValue: -1);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task OpportunisticAsync_NoSlotAvailable_ReturnsFallback()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = await limiter.TryAcquireAsync();
        Assert.True(first);

        int result = await limiter.OpportunisticAsync(
            () => Task.FromResult(42),
            fallbackValue: -1,
            timeout: TimeSpan.FromMilliseconds(10));

        Assert.Equal(-1, result);
        limiter.Release();
    }

    // =================================================================
    // GetStats
    // =================================================================

    [Fact]
    public void GetStats_InitialState_AllZeros()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 3);
        XpdLimiterStats stats = limiter.GetStats();

        Assert.Equal(3, stats.MaxConcurrent);
        Assert.Equal(0, stats.ActiveQueries);
        Assert.Equal(0, stats.TotalQueries);
        Assert.Equal(0, stats.SkippedQueries);
        Assert.Equal(0, stats.SkipRatePercent);
        Assert.Equal(0, stats.AvgWaitTimeMs);
    }

    [Fact]
    public void GetStats_AfterAcquireRelease_CountsCorrectly()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        bool acquired = limiter.TryAcquire();
        Assert.True(acquired);

        XpdLimiterStats midStats = limiter.GetStats();
        Assert.Equal(1, midStats.ActiveQueries);
        Assert.Equal(1, midStats.TotalQueries);
        Assert.Equal(0, midStats.SkippedQueries);

        limiter.Release();

        XpdLimiterStats afterStats = limiter.GetStats();
        Assert.Equal(0, afterStats.ActiveQueries);
        Assert.Equal(1, afterStats.TotalQueries);
    }

    [Fact]
    public void GetStats_SkippedQueries_TrackedCorrectly()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 1);
        bool first = limiter.TryAcquire();
        Assert.True(first);

        // These should all be skipped
        bool skip1 = limiter.TryAcquire(TimeSpan.FromMilliseconds(1));
        bool skip2 = limiter.TryAcquire(TimeSpan.FromMilliseconds(1));
        Assert.False(skip1);
        Assert.False(skip2);

        XpdLimiterStats stats = limiter.GetStats();
        Assert.Equal(1, stats.TotalQueries);
        Assert.Equal(2, stats.SkippedQueries);
        Assert.True(stats.SkipRatePercent > 60); // 2 / 3 = 66.7%

        limiter.Release();
    }

    // =================================================================
    // ResetStats
    // =================================================================

    [Fact]
    public void ResetStats_ClearsCounters()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        limiter.TryAcquire();
        limiter.Release();
        limiter.TryAcquire();
        limiter.Release();

        limiter.ResetStats();
        XpdLimiterStats stats = limiter.GetStats();

        Assert.Equal(0, stats.TotalQueries);
        Assert.Equal(0, stats.SkippedQueries);
        Assert.Equal(0, stats.AvgWaitTimeMs);
    }

    // =================================================================
    // Concurrency
    // =================================================================

    [Fact]
    public async Task Concurrent_MultipleThreads_LimitsCorrectly()
    {
        XpdConcurrencyLimiter limiter = new(maxConcurrent: 2);
        int concurrentCount = 0;
        int maxObservedConcurrent = 0;
        object lockObj = new();

        Task[] tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            bool acquired = await limiter.TryAcquireAsync(timeout: TimeSpan.FromSeconds(1));
            if (acquired)
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    if (concurrentCount > maxObservedConcurrent)
                        maxObservedConcurrent = concurrentCount;
                }

                await Task.Delay(50);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                limiter.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // Maximum observed concurrent should never exceed limit
        Assert.True(maxObservedConcurrent <= 2,
            $"Max observed concurrency was {maxObservedConcurrent}, expected <= 2");
    }

    // =================================================================
    // Constructor Defaults
    // =================================================================

    [Fact]
    public void Constructor_DefaultMax_IsTwo()
    {
        XpdConcurrencyLimiter limiter = new();
        XpdLimiterStats stats = limiter.GetStats();
        Assert.Equal(2, stats.MaxConcurrent);
    }
}
