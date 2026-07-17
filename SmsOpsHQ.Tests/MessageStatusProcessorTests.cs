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

/// <summary>
/// Tests for <see cref="MessageStatusProcessor"/> — applies a Twilio delivery
/// status callback to an existing outbound message and pushes a realtime update.
/// </summary>
public class MessageStatusProcessorTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MessageStatusProcessor _processor;
    private readonly RecordingRealtimeService _realtime = new();
    private readonly MessageRepository _messageRepo;
    private readonly ReviewRepository _reviewRepo;
    private readonly int _storeId;
    private readonly int _threadId;

    public MessageStatusProcessorTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        StoreEntity store = new() { StoreName = "Test", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        ThreadEntity thread = new() { StoreId = _storeId, Status = "Open" };
        _db.Threads.Add(thread);
        _db.SaveChanges();
        _threadId = thread.ThreadId;

        _messageRepo = new MessageRepository(_db);
        _reviewRepo = new ReviewRepository(_db);
        _processor = new MessageStatusProcessor(
            _messageRepo,
            _reviewRepo,
            _realtime,
            NullLogger<MessageStatusProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private async Task<int> SeedOutboundMessageAsync(string sid)
    {
        SmsOpsHQ.Core.Entities.Message msg = await _messageRepo.CreateOutboundAsync(
            _storeId, _threadId, storePhone: "+15551234567",
            fromE164: "+15551234567", toE164: "+15559876543",
            body: "hello", mediaJson: null, category: "general",
            sentByUserId: null);
        await _messageRepo.UpdateSentAsync(msg.MessageId, sid, "Sent");
        return msg.MessageId;
    }

    [Fact]
    public async Task ProcessAsync_KnownSid_UpdatesStatus_PushesRealtime_ReturnsUpdated()
    {
        int messageId = await SeedOutboundMessageAsync("SM_known");

        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_known",
            MessageStatus = "delivered",
            ErrorCode = null,
        });

        Assert.Equal(MessageStatusResultKind.Updated, result.Kind);
        Assert.Equal(_storeId, result.StoreId);
        Assert.Equal(_threadId, result.ThreadId);
        Assert.Equal(messageId, result.MessageId);

        MessageEntity row = await _db.Messages.AsNoTracking().SingleAsync(m => m.MessageId == messageId);
        Assert.Equal("Delivered", row.Status); // capitalized

        Assert.Single(_realtime.StatusPushes);
        Assert.Equal("Delivered", _realtime.StatusPushes[0].Status);
    }

    [Fact]
    public async Task ProcessAsync_PreservesErrorCodeOnFailedStatus()
    {
        int messageId = await SeedOutboundMessageAsync("SM_fail");

        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_fail",
            MessageStatus = "failed",
            ErrorCode = "30007",
        });

        Assert.Equal(MessageStatusResultKind.Updated, result.Kind);
        MessageEntity row = await _db.Messages.AsNoTracking().SingleAsync(m => m.MessageId == messageId);
        Assert.Equal("Failed", row.Status);
        Assert.Equal("30007", row.ErrorCode);

        Assert.Equal("30007", _realtime.StatusPushes[0].ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_UnknownSid_ReturnsNotFound_DoesNotPushRealtime()
    {
        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_does_not_exist",
            MessageStatus = "delivered",
        });

        Assert.Equal(MessageStatusResultKind.NotFound, result.Kind);
        Assert.Empty(_realtime.StatusPushes);
    }

    [Fact]
    public async Task ProcessAsync_EmptySid_ReturnsEmpty()
    {
        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "",
            MessageStatus = "delivered",
        });

        Assert.Equal(MessageStatusResultKind.Empty, result.Kind);
        Assert.Empty(_realtime.StatusPushes);
    }

    [Fact]
    public async Task ProcessAsync_EmptyStatus_ReturnsEmpty()
    {
        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_anything",
            MessageStatus = "",
        });

        Assert.Equal(MessageStatusResultKind.Empty, result.Kind);
    }

    [Fact]
    public async Task ProcessAsync_NullUpdate_ReturnsErrorWithoutThrowing()
    {
        MessageStatusProcessingResult result = await _processor.ProcessAsync(null!);
        Assert.Equal(MessageStatusResultKind.Error, result.Kind);
    }

    [Fact]
    public async Task ProcessAsync_RepeatedDeliveredStatus_IsIdempotent()
    {
        int messageId = await SeedOutboundMessageAsync("SM_repeat");

        for (int i = 0; i < 3; i++)
        {
            await _processor.ProcessAsync(new MessageStatusUpdate
            {
                MessageSid = "SM_repeat",
                MessageStatus = "delivered",
            });
        }

        MessageEntity row = await _db.Messages.AsNoTracking().SingleAsync(m => m.MessageId == messageId);
        Assert.Equal("Delivered", row.Status);
        // Three callbacks -> three realtime pushes is expected (existing behavior);
        // the DB row stays consistent.
        Assert.Equal(3, _realtime.StatusPushes.Count);
    }

    [Fact]
    public async Task ProcessAsync_ReviewDelivered_UpdatesReviewAndDeliveredAt()
    {
        int reviewId = await SeedReviewRequestAsync("SM_review_delivered");
        DateTime receivedAt = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_review_delivered",
            MessageStatus = "delivered",
            ReceivedAtUtc = receivedAt
        });

        Assert.Equal(MessageStatusResultKind.Updated, result.Kind);
        Assert.Equal($"review_request_updated:{reviewId}", result.Reason);
        ReviewRequestEntity row = await _db.ReviewRequests.AsNoTracking()
            .SingleAsync(r => r.ReviewRequestId == reviewId);
        Assert.Equal("Delivered", row.Status);
        Assert.Equal("delivered", row.ProviderStatus);
        Assert.Equal(receivedAt, row.DeliveredAt);
        Assert.Null(row.ErrorCode);
        Assert.Null(row.ErrorMessage);
        Assert.Empty(_realtime.StatusPushes);
    }

    [Fact]
    public async Task ProcessAsync_ReviewUndelivered_UpdatesErrorFields()
    {
        int reviewId = await SeedReviewRequestAsync("SM_review_undelivered");

        MessageStatusProcessingResult result = await _processor.ProcessAsync(new MessageStatusUpdate
        {
            MessageSid = "SM_review_undelivered",
            MessageStatus = "undelivered",
            ErrorCode = "30007"
        });

        Assert.Equal(MessageStatusResultKind.Updated, result.Kind);
        ReviewRequestEntity row = await _db.ReviewRequests.AsNoTracking()
            .SingleAsync(r => r.ReviewRequestId == reviewId);
        Assert.Equal("Undelivered", row.Status);
        Assert.Equal("undelivered", row.ProviderStatus);
        Assert.Equal("30007", row.ErrorCode);
        Assert.Contains("30007", row.ErrorMessage);
        Assert.Null(row.DeliveredAt);
    }

    private async Task<int> SeedReviewRequestAsync(string sid)
    {
        CustomerEntity customer = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15559876543"
        };
        ReviewChannelEntity channel = new()
        {
            StoreId = _storeId,
            PlatformName = "Google",
            ReviewUrl = "https://reviews.example.test",
            IsActive = true
        };
        TemplateEntity template = new()
        {
            StoreId = _storeId,
            Name = "Review",
            Body = "Please review us",
            Category = "Review"
        };
        _db.AddRange(customer, channel, template);
        await _db.SaveChangesAsync();

        ReviewRequestEntity review = new()
        {
            StoreId = _storeId,
            CustomerId = customer.CustomerId,
            PhoneE164 = customer.PhoneE164,
            ReviewChannelId = channel.ReviewChannelId,
            TemplateId = template.TemplateId,
            MessageBody = template.Body,
            TwilioSid = sid,
            Status = "Accepted",
            ProviderStatus = "queued",
            SentAt = DateTime.UtcNow
        };
        _db.ReviewRequests.Add(review);
        await _db.SaveChangesAsync();
        return review.ReviewRequestId;
    }

    private sealed class RecordingRealtimeService : IRealtimeService
    {
        public List<(int StoreId, int ThreadId, int MessageId, string Sid, string Status, string? ErrorCode)> StatusPushes { get; } = new();

        public Task PushMessageNewAsync(int storeId, int threadId, MessageDto message, ThreadDto thread, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PushMessageStatusAsync(int storeId, int threadId, int messageId, string twilioSid, string status, string? errorCode, CancellationToken cancellationToken = default)
        {
            StatusPushes.Add((storeId, threadId, messageId, twilioSid, status, errorCode));
            return Task.CompletedTask;
        }

        public Task PushSystemAlertAsync(int storeId, string code, string message, string severity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
