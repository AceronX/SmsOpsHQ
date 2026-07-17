using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class ReviewServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeTwilioService _twilio = new();
    private readonly ReviewService _service;
    private readonly StoreEntity _store;
    private readonly TwilioNumberEntity _defaultNumber;

    public ReviewServiceTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        _store = new StoreEntity { StoreName = "Test Shop", IsActive = true };
        _db.Stores.Add(_store);
        _db.SaveChanges();

        _defaultNumber = new TwilioNumberEntity
        {
            StoreId = _store.StoreId,
            PhoneE164 = "+15551110010",
            IsActive = true
        };
        _db.TwilioNumbers.Add(_defaultNumber);
        _db.SaveChanges();
        _store.DefaultNumberId = _defaultNumber.NumberId;

        _db.ReviewChannels.Add(new ReviewChannelEntity
        {
            StoreId = _store.StoreId,
            PlatformName = "Google",
            ReviewUrl = "https://reviews.example.test/store",
            SortOrder = 0,
            IsActive = true
        });
        _db.Templates.Add(new TemplateEntity
        {
            StoreId = _store.StoreId,
            Name = "Review request",
            Body = "Please review {store}: {link}",
            Category = "Review"
        });
        _db.SaveChanges();

        _service = new ReviewService(
            new ReviewRepository(_db),
            new TemplateRepository(_db),
            new CustomerRepository(_db),
            new StoreRepository(_db),
            new OutboundNumberResolver(_db),
            _twilio,
            NullLogger<ReviewService>.Instance);
    }

    [Fact]
    public async Task SendReviewRequestAsync_NoChannel_ThrowsBeforeSending()
    {
        await _db.ReviewChannels.ExecuteDeleteAsync();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SendReviewRequestAsync(_store.StoreId, "+15552220001"));

        Assert.Contains("No review channels", ex.Message);
        Assert.Empty(_twilio.Calls);
    }

    [Fact]
    public async Task SendReviewRequestAsync_NoReviewTemplate_ThrowsBeforeSending()
    {
        await _db.Templates.ExecuteDeleteAsync();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SendReviewRequestAsync(_store.StoreId, "+15552220002"));

        Assert.Contains("No review templates", ex.Message);
        Assert.Empty(_twilio.Calls);
    }

    [Fact]
    public async Task SendReviewRequestAsync_NoSenderNumber_ThrowsBeforeSending()
    {
        _store.DefaultNumberId = 0;
        await _db.SaveChangesAsync();

        OutboundNumberValidationException ex = await Assert.ThrowsAsync<OutboundNumberValidationException>(
            () => _service.SendReviewRequestAsync(_store.StoreId, "+15552220003"));

        Assert.Contains("No default", ex.Message);
        Assert.Empty(_twilio.Calls);
    }

    [Fact]
    public async Task SendReviewRequestAsync_CrossStoreSender_ThrowsWithoutFallback()
    {
        StoreEntity otherStore = new() { StoreName = "Other", IsActive = true };
        _db.Stores.Add(otherStore);
        await _db.SaveChangesAsync();
        TwilioNumberEntity otherNumber = new()
        {
            StoreId = otherStore.StoreId,
            PhoneE164 = "+15551110011",
            IsActive = true
        };
        _db.TwilioNumbers.Add(otherNumber);
        await _db.SaveChangesAsync();

        OutboundNumberValidationException ex = await Assert.ThrowsAsync<OutboundNumberValidationException>(
            () => _service.SendReviewRequestAsync(
                _store.StoreId, "+15552220004", otherNumber.NumberId));

        Assert.Contains("does not belong", ex.Message);
        Assert.Empty(_twilio.Calls);
    }

    [Fact]
    public async Task SendReviewRequestAsync_MockResult_IsPersistedThenThrowsTypedError()
    {
        _twilio.IsMockModeValue = true;
        _twilio.Result = new TwilioSendResult
        {
            Success = true,
            IsMock = true,
            TwilioSid = "SM_MOCK_REVIEW",
            Status = "Mock",
            ErrorCode = "MOCK_MODE",
            ErrorMessage = "Not delivered in mock mode."
        };

        OutboundSendException ex = await Assert.ThrowsAsync<OutboundSendException>(
            () => _service.SendReviewRequestAsync(_store.StoreId, "+15552220005"));

        Assert.True(ex.Attempt.IsMock);
        Assert.Equal("Mock", ex.Attempt.Status);
        ReviewRequestEntity row = await _db.ReviewRequests.AsNoTracking().SingleAsync();
        Assert.Equal("Mock", row.Status);
        Assert.Equal("MOCK_MODE", row.ErrorCode);
        Assert.Equal("Not delivered in mock mode.", row.ErrorMessage);
    }

    [Fact]
    public async Task SendReviewRequestAsync_TwilioFailure_IsPersistedThenThrowsTypedError()
    {
        _twilio.Result = new TwilioSendResult
        {
            Success = false,
            Status = "Failed",
            ErrorCode = "30007",
            ErrorMessage = "Carrier violation"
        };

        OutboundSendException ex = await Assert.ThrowsAsync<OutboundSendException>(
            () => _service.SendReviewRequestAsync(_store.StoreId, "+15552220006"));

        Assert.False(ex.Attempt.IsMock);
        Assert.Equal("Failed", ex.Attempt.Status);
        ReviewRequestEntity row = await _db.ReviewRequests.AsNoTracking().SingleAsync();
        Assert.Equal("Failed", row.Status);
        Assert.Equal("30007", row.ErrorCode);
        Assert.Equal("Carrier violation", row.ErrorMessage);
    }

    [Fact]
    public async Task SendReviewRequestAsync_Accepted_UsesSelectedSenderAndReturnsAccepted()
    {
        TwilioNumberEntity selected = new()
        {
            StoreId = _store.StoreId,
            PhoneE164 = "+15551110012",
            IsActive = true
        };
        _db.TwilioNumbers.Add(selected);
        await _db.SaveChangesAsync();
        _twilio.Result = new TwilioSendResult
        {
            Success = true,
            TwilioSid = "SM_ACCEPTED_REVIEW",
            Status = "queued"
        };

        ReviewRequestDto result = await _service.SendReviewRequestAsync(
            _store.StoreId, "+15552220007", selected.NumberId);

        Assert.Equal("Accepted", result.Status);
        Assert.Equal("queued", result.ProviderStatus);
        Assert.False(result.IsMock);
        Assert.Equal(selected.PhoneE164, Assert.Single(_twilio.Calls).From);
        ReviewRequestEntity row = await _db.ReviewRequests.AsNoTracking().SingleAsync();
        Assert.Equal("Accepted", row.Status);
        Assert.Equal("queued", row.ProviderStatus);
        Assert.Equal("SM_ACCEPTED_REVIEW", row.TwilioSid);
    }

    [Fact]
    public async Task GetReadinessAsync_ReturnsEachFailedPrerequisite()
    {
        _twilio.IsMockModeValue = true;
        _store.DefaultNumberId = 0;
        await _db.SaveChangesAsync();
        await _db.ReviewChannels.ExecuteDeleteAsync();
        await _db.Templates.ExecuteDeleteAsync();

        ReviewReadinessDto result = await _service.GetReadinessAsync(_store.StoreId);

        Assert.False(result.Ready);
        Assert.Equal(5, result.Checks.Count);
        Assert.True(result.Checks.Single(c => c.Code == "store").Passed);
        Assert.False(result.Checks.Single(c => c.Code == "sender").Passed);
        Assert.False(result.Checks.Single(c => c.Code == "twilio").Passed);
        Assert.False(result.Checks.Single(c => c.Code == "channel").Passed);
        Assert.False(result.Checks.Single(c => c.Code == "template").Passed);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private sealed class FakeTwilioService : ITwilioService
    {
        public bool IsMockModeValue { get; set; }
        public bool IsMockMode => IsMockModeValue;
        public string AccountSidPrefix => IsMockMode ? string.Empty : "AC_TEST";
        public bool HasMessagingService => false;
        public TwilioSendResult Result { get; set; } = new()
        {
            Success = true,
            TwilioSid = "SM_DEFAULT",
            Status = "queued"
        };
        public List<(string From, string To, string Body)> Calls { get; } = new();

        public Task<TwilioSendResult> SendSmsAsync(
            string fromE164,
            string toE164,
            string body,
            List<string>? mediaUrls = null,
            string? statusCallbackUrl = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fromE164, toE164, body));
            return Task.FromResult(Result);
        }
    }
}
