using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

/// <summary>
/// Tests for <see cref="InboundSmsProcessor"/> — the store-side pipeline that
/// the legacy HTTP webhook and the Phase 5 SignalR receiver both call.
/// Uses real repositories + SQLite so business rules and EF behavior
/// are exercised end-to-end; only the SignalR push and XPD identity lookup
/// are faked.
/// </summary>
public class InboundSmsProcessorTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly InboundSmsProcessor _processor;
    private readonly RecordingRealtimeService _realtime = new();
    private readonly StubIdentityResolver _identity = new();
    private readonly int _storeId;

    private const string StorePhone = "+15551234567";
    private const string CustomerPhone = "+15559876543";

    public InboundSmsProcessorTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        StoreEntity store = new() { StoreName = "Test Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity number = new()
        {
            StoreId = _storeId,
            PhoneE164 = StorePhone,
            FriendlyName = "Main",
            IsActive = true,
        };
        _db.TwilioNumbers.Add(number);
        _db.SaveChanges();

        store.DefaultNumberId = number.NumberId;
        _db.SaveChanges();

        StoreRepository storeRepo = new(_db);
        MessageRepository messageRepo = new(_db);
        ThreadRepository threadRepo = new(_db);
        CustomerRepository customerRepo = new(_db);
        OptOutRepository optOutRepo = new(_db);

        StorePhoneResolver storePhoneResolver = new(storeRepo);
        PhoneValidationService validation = new();
        QuarantineService quarantine = new(_db);

        _processor = new InboundSmsProcessor(
            messageRepo, threadRepo, customerRepo, optOutRepo,
            storePhoneResolver, validation, quarantine, _identity, _realtime,
            NullLogger<InboundSmsProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private static InboundSmsRequest Request(string sid = "SM_1", string body = "hello", string? to = null, string? from = null) => new()
    {
        MessageSid = sid,
        From = from ?? CustomerPhone,
        To = to ?? StorePhone,
        Body = body,
        NumMedia = 0,
        ReceivedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task ProcessAsync_NewMessage_CreatesInboundRecord_PushesRealtime_ReturnsProcessed()
    {
        InboundSmsProcessingResult result = await _processor.ProcessAsync(Request());

        Assert.Equal(InboundSmsResultKind.Processed, result.Kind);
        Assert.Equal(_storeId, result.StoreId);
        Assert.NotNull(result.ThreadId);
        Assert.NotNull(result.MessageId);

        MessageEntity? row = await _db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == result.MessageId);
        Assert.NotNull(row);
        Assert.Equal("Inbound", row!.Direction);
        Assert.Equal("Received", row.Status);
        Assert.Equal("SM_1", row.TwilioSid);
        Assert.Equal(StorePhone, row.StorePhone);

        Assert.Single(_realtime.MessageNewPushes);
        (int pushedStoreId, int pushedThreadId, _, _) = _realtime.MessageNewPushes[0];
        Assert.Equal(_storeId, pushedStoreId);
        Assert.Equal(result.ThreadId, pushedThreadId);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateSid_ReturnsDuplicate_DoesNotCreateSecondRow()
    {
        InboundSmsProcessingResult first = await _processor.ProcessAsync(Request());
        InboundSmsProcessingResult second = await _processor.ProcessAsync(Request());

        Assert.Equal(InboundSmsResultKind.Processed, first.Kind);
        Assert.Equal(InboundSmsResultKind.Duplicate, second.Kind);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await _db.Messages.CountAsync(m => m.TwilioSid == "SM_1"));

        // The duplicate must not push a phantom "MessageNew" event.
        Assert.Single(_realtime.MessageNewPushes);
    }

    [Fact]
    public async Task ProcessAsync_NoStoreMatch_ReturnsNoStoreMatch_WritesNothing()
    {
        InboundSmsProcessingResult result = await _processor.ProcessAsync(
            Request(sid: "SM_no_match", to: "+15550009999", from: "+15550008888"));

        Assert.Equal(InboundSmsResultKind.NoStoreMatch, result.Kind);
        Assert.Null(result.StoreId);
        Assert.Equal(0, await _db.Messages.CountAsync());
        Assert.Empty(_realtime.MessageNewPushes);
    }

    [Fact]
    public async Task ProcessAsync_MessageBodyContainsStorePhone_QuarantinesAndReturnsQuarantined()
    {
        // Body validation: leaked store phone -> quarantine
        InboundSmsProcessingResult result = await _processor.ProcessAsync(
            Request(sid: "SM_leak", body: $"Call us at {StorePhone}"));

        Assert.Equal(InboundSmsResultKind.Quarantined, result.Kind);
        Assert.Equal(_storeId, result.StoreId);
        Assert.NotNull(result.MessageId); // quarantine id, not message id
        Assert.Equal(1, await _db.QuarantinedMessages.CountAsync(q => q.TwilioSid == "SM_leak"));
        Assert.Equal(0, await _db.Messages.CountAsync(m => m.TwilioSid == "SM_leak"));
        Assert.Empty(_realtime.MessageNewPushes);
    }

    [Fact]
    public async Task ProcessAsync_StopKeyword_AddsOptOut_AndStillRecordsMessage()
    {
        InboundSmsProcessingResult result = await _processor.ProcessAsync(Request(sid: "SM_stop", body: "STOP"));

        Assert.Equal(InboundSmsResultKind.Processed, result.Kind);
        Assert.Equal(1, await _db.OptOuts.CountAsync(o => o.StoreId == _storeId && o.PhoneE164 == CustomerPhone));
    }

    [Theory]
    [InlineData("UNSUBSCRIBE")]
    [InlineData("Cancel")]
    [InlineData("  end  ")]
    [InlineData("quit")]
    public async Task ProcessAsync_StopKeywordVariants_AreRecognized(string body)
    {
        await _processor.ProcessAsync(Request(sid: $"SM_{body.Trim()}", body: body));
        Assert.Equal(1, await _db.OptOuts.CountAsync(o => o.PhoneE164 == CustomerPhone));
    }

    [Fact]
    public async Task ProcessAsync_NewSender_FindsOrCreatesCustomer_AndThread()
    {
        InboundSmsProcessingResult result = await _processor.ProcessAsync(Request());

        CustomerEntity customer = await _db.Customers.AsNoTracking()
            .SingleAsync(c => c.StoreId == _storeId && c.PhoneE164 == CustomerPhone);
        Assert.True(customer.CustomerId > 0);

        ThreadEntity thread = await _db.Threads.AsNoTracking().SingleAsync(t => t.ThreadId == result.ThreadId);
        Assert.Equal(_storeId, thread.StoreId);
        Assert.Equal(customer.CustomerId, thread.CustomerId);
        Assert.Equal(CustomerPhone, thread.ContactPhoneE164);
        Assert.NotNull(thread.TwilioNumberId);
        Assert.Equal(1, thread.UnreadCount);
    }

    [Fact]
    public async Task ProcessAsync_SecondMessageFromSameSender_ReusesThread_IncrementsUnread()
    {
        InboundSmsProcessingResult first = await _processor.ProcessAsync(Request(sid: "SM_a"));
        InboundSmsProcessingResult second = await _processor.ProcessAsync(Request(sid: "SM_b"));

        Assert.Equal(first.ThreadId, second.ThreadId);
        ThreadEntity thread = await _db.Threads.AsNoTracking().SingleAsync(t => t.ThreadId == first.ThreadId);
        Assert.Equal(2, thread.UnreadCount);
    }

    [Fact]
    public async Task ProcessAsync_MatchesExistingOutboundConversationKey()
    {
        TwilioNumberEntity number = await _db.TwilioNumbers.AsNoTracking()
            .SingleAsync(n => n.StoreId == _storeId && n.PhoneE164 == StorePhone);
        CustomerRepository customerRepo = new(_db);
        Customer customer = await customerRepo.FindOrCreateAsync(_storeId, CustomerPhone);
        ThreadRepository threadRepo = new(_db);
        Core.Entities.Thread outboundThread = await threadRepo.FindOrCreateAsync(
            _storeId,
            number.NumberId,
            CustomerPhone,
            identityId: null,
            customerId: customer.CustomerId);

        InboundSmsProcessingResult inbound = await _processor.ProcessAsync(
            Request(sid: "SM_matches_outbound"));

        Assert.Equal(outboundThread.ThreadId, inbound.ThreadId);
        Assert.Single(await _db.Threads.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_MmsWithMedia_PersistsMediaJson()
    {
        InboundSmsRequest req = Request(sid: "SM_mms", body: "see attached");
        req.NumMedia = 2;
        req.Media.Add(new InboundMediaItem { Index = 0, Url = "https://twilio/media/A", ContentType = "image/jpeg" });
        req.Media.Add(new InboundMediaItem { Index = 1, Url = "https://twilio/media/B", ContentType = "image/png" });

        InboundSmsProcessingResult result = await _processor.ProcessAsync(req);

        MessageEntity row = await _db.Messages.AsNoTracking().SingleAsync(m => m.MessageId == result.MessageId);
        Assert.False(string.IsNullOrEmpty(row.MediaJson));
        Assert.Contains("twilio/media/A", row.MediaJson, StringComparison.Ordinal);
        Assert.Contains("image/jpeg", row.MediaJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MmsWithMissingUrl_SkipsBlankEntries()
    {
        InboundSmsRequest req = Request(sid: "SM_mms2");
        req.NumMedia = 2;
        req.Media.Add(new InboundMediaItem { Index = 0, Url = "https://twilio/media/A", ContentType = null });
        req.Media.Add(new InboundMediaItem { Index = 1, Url = "", ContentType = null });

        InboundSmsProcessingResult result = await _processor.ProcessAsync(req);

        MessageEntity row = await _db.Messages.AsNoTracking().SingleAsync(m => m.MessageId == result.MessageId);
        Assert.NotNull(row.MediaJson);
        Assert.Contains("twilio/media/A", row.MediaJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_NullRequest_ReturnsErrorWithoutThrowing()
    {
        InboundSmsProcessingResult result = await _processor.ProcessAsync(null!);
        Assert.Equal(InboundSmsResultKind.Error, result.Kind);
    }

    [Fact]
    public async Task ProcessAsync_ResolvedIdentity_StoredOnThread()
    {
        _identity.Map[(_storeId, CustomerPhone)] = 9999;

        InboundSmsProcessingResult result = await _processor.ProcessAsync(Request(sid: "SM_known"));

        ThreadEntity thread = await _db.Threads.AsNoTracking().SingleAsync(t => t.ThreadId == result.ThreadId);
        Assert.Equal(9999, thread.IdentityId);
    }

    // -------- fakes --------

    private sealed class RecordingRealtimeService : IRealtimeService
    {
        public List<(int StoreId, int ThreadId, MessageDto Msg, ThreadDto Thread)> MessageNewPushes { get; } = new();
        public List<(int StoreId, int ThreadId, int MessageId, string Sid, string Status, string? ErrorCode)> StatusPushes { get; } = new();

        public Task PushMessageNewAsync(int storeId, int threadId, MessageDto message, ThreadDto thread, CancellationToken cancellationToken = default)
        {
            MessageNewPushes.Add((storeId, threadId, message, thread));
            return Task.CompletedTask;
        }
        public Task PushMessageStatusAsync(int storeId, int threadId, int messageId, string twilioSid, string status, string? errorCode, CancellationToken cancellationToken = default)
        {
            StatusPushes.Add((storeId, threadId, messageId, twilioSid, status, errorCode));
            return Task.CompletedTask;
        }
        public Task PushSystemAlertAsync(int storeId, string code, string message, string severity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubIdentityResolver : IIdentityResolver
    {
        // Deterministic test stub: lookup table from (storeId, phone) to identity id.
        public Dictionary<(int StoreId, string Phone), int> Map { get; } = new();

        public Task<int?> ResolveIdentityIdAsync(int storeId, string phoneE164, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Map.TryGetValue((storeId, phoneE164), out int id) ? id : (int?)null);
        }

        public Task<List<int>> ResolveCustomerKeysAsync(string phoneE164, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());

        public Task<List<Core.Models.CustomerPhoneMatch>> ResolveCustomerPhoneMatchesAsync(string phoneE164, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Core.Models.CustomerPhoneMatch>());
    }
}
