using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for ReminderService: sending, exclusions, history, and batch logic.
public class ReminderServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ReminderService _service;
    private readonly int _storeId;

    public ReminderServiceTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        // Seed store + Twilio number
        StoreEntity store = new StoreEntity
        {
            StoreName = "Reminder Test Store",
            IsActive = true
        };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity twilioNum = new TwilioNumberEntity
        {
            PhoneE164 = "+13479527212",
            FriendlyName = "Test",
            StoreId = _storeId,
            IsActive = true
        };
        _db.TwilioNumbers.Add(twilioNum);
        _db.SaveChanges();

        store.DefaultNumberId = twilioNum.NumberId;
        _db.SaveChanges();

        ILogger<TwilioService> twilioLogger = NullLogger<TwilioService>.Instance;
        TwilioSettings twilioSettings = new TwilioSettings();
        ITwilioService twilioService = new TwilioService(
            Options.Create(twilioSettings), twilioLogger);

        IStorePhoneResolver storePhoneResolver = new StorePhoneResolver(
            new TestStoreRepository(_db));

        ILogger<ReminderService> logger = NullLogger<ReminderService>.Instance;

        IConfiguration testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Reminders:TestMode", "true" }
            })
            .Build();

        _service = new ReminderService(_db, twilioService, storePhoneResolver, logger,
            configuration: testConfig);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── Exclusion / Unsubscribe Tests ─────────────────────────────────

    [Fact]
    public async Task IsPhoneExcludedAsync_ReturnsFalse_WhenNotExcluded()
    {
        bool result = await _service.IsPhoneExcludedAsync("+17185551234");
        Assert.False(result);
    }

    [Fact]
    public async Task IsPhoneExcludedAsync_ReturnsTrue_WhenExcluded()
    {
        await _service.AddToExcludedAsync("+17185551234", "Test exclusion", null);
        bool result = await _service.IsPhoneExcludedAsync("+17185551234");
        Assert.True(result);
    }

    [Fact]
    public async Task IsPhoneExcludedAsync_ReturnsTrue_WhenUnsubscribed()
    {
        await _service.AddToUnsubscribedAsync("+17185551234", "STOP", "Customer opted out");
        bool result = await _service.IsPhoneExcludedAsync("+17185551234");
        Assert.True(result);
    }

    [Fact]
    public async Task IsPhoneExcludedAsync_ReturnsTrue_ForInvalidPhone()
    {
        bool result = await _service.IsPhoneExcludedAsync("abc");
        Assert.True(result);
    }

    [Fact]
    public async Task AddToExcludedAsync_ReturnsFalse_ForInvalidPhone()
    {
        bool result = await _service.AddToExcludedAsync("abc", "Bad", null);
        Assert.False(result);
    }

    [Fact]
    public async Task AddToExcludedAsync_Idempotent_DoesNotDuplicate()
    {
        await _service.AddToExcludedAsync("+17185551234", "First", null);
        await _service.AddToExcludedAsync("+17185551234", "Second", null);

        int count = await _db.SmsExcluded.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AddToUnsubscribedAsync_Idempotent_DoesNotDuplicate()
    {
        await _service.AddToUnsubscribedAsync("+17185551234", "STOP");
        await _service.AddToUnsubscribedAsync("+17185551234", "MANUAL");

        int count = await _db.SmsUnsubscribed.CountAsync();
        Assert.Equal(1, count);
    }

    // ── Send Reminder Tests ──────────────────────────────────────────

    [Fact]
    public async Task SendReminderAsync_ExcludedPhone_ReturnsFalse()
    {
        await _service.AddToExcludedAsync("+17185551234", "Excluded", null);

        ReminderSendResult result = await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1,
            CustomerKey = 1,
            Phone = "+17185551234",
            TransNo = "1001",
            DueDate = "6/1/2026",
            DaysDiff = -7,
            StoreId = _storeId
        });

        Assert.False(result.Success);
        Assert.Contains("excluded", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendReminderAsync_InvalidPhone_ReturnsFalse()
    {
        ReminderSendResult result = await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1,
            CustomerKey = 1,
            Phone = "abc",
            TransNo = "1001",
            DueDate = "6/1/2026",
            DaysDiff = -7,
            StoreId = _storeId
        });

        // Invalid phone is excluded by IsPhoneExcludedAsync
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SendReminderAsync_ValidPhone_TestMode_LogsAndReturnsTrue()
    {
        ReminderSendResult result = await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 100,
            CustomerKey = 50,
            Phone = "+17185559876",
            TransNo = "2001",
            DueDate = "6/1/2026",
            DaysDiff = -7,
            StoreId = _storeId
        });

        Assert.True(result.Success);
        Assert.True(result.TestMode);
        Assert.Equal("reminder_-7", result.ReminderType);
        Assert.NotNull(result.TwilioSid);
        Assert.StartsWith("TEST_", result.TwilioSid);

        // Verify logged in DB
        SmsReminderEntity? reminder = await _db.SmsReminders
            .FirstOrDefaultAsync(r => r.TicketKey == 100);
        Assert.NotNull(reminder);
        Assert.Equal(1, reminder.Status);
        Assert.Equal("reminder_-7", reminder.ReminderType);
    }

    [Fact]
    public async Task SendReminderAsync_DuplicateReminder_ReturnsFalse()
    {
        SendReminderRequest request = new()
        {
            TicketKey = 100,
            CustomerKey = 50,
            Phone = "+17185559876",
            TransNo = "2001",
            DueDate = "6/1/2026",
            DaysDiff = 0,
            StoreId = _storeId
        };

        ReminderSendResult first = await _service.SendReminderAsync(request);
        Assert.True(first.Success);

        ReminderSendResult second = await _service.SendReminderAsync(request);
        Assert.False(second.Success);
        Assert.Contains("already sent", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendReminderAsync_InvalidDaysDiff_ReturnsFalse()
    {
        ReminderSendResult result = await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1,
            CustomerKey = 1,
            Phone = "+17185559876",
            TransNo = "1001",
            DueDate = "6/1/2026",
            DaysDiff = 99,
            StoreId = _storeId
        });

        Assert.False(result.Success);
        Assert.Contains("No template", result.Message);
    }

    // ── Get Next Reminder Type ───────────────────────────────────────

    [Fact]
    public async Task GetNextReminderTypeAsync_NoSent_ReturnsEarliestApplicable()
    {
        NextReminderResult? result =
            await _service.GetNextReminderTypeAsync(1, "6/1/2026", daysLate: 0);

        Assert.NotNull(result);
        Assert.Equal("reminder_-7", result.ReminderType);
        Assert.Equal(-7, result.DaysDiff);
    }

    [Fact]
    public async Task GetNextReminderTypeAsync_AfterSending_ReturnsNext()
    {
        // Send the -7 day reminder first
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "1001", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });

        NextReminderResult? result =
            await _service.GetNextReminderTypeAsync(1, "6/1/2026", daysLate: 0);

        Assert.NotNull(result);
        Assert.Equal("reminder_0", result.ReminderType);
    }

    [Fact]
    public async Task GetNextReminderTypeAsync_AllSent_ReturnsNull()
    {
        foreach (int daysDiff in new[] { -7, 0, 7, 14, 30 })
        {
            await _service.SendReminderAsync(new SendReminderRequest
            {
                TicketKey = 1, CustomerKey = 1, Phone = "+17185559876",
                TransNo = "1001", DueDate = "6/1/2026", DaysDiff = daysDiff, StoreId = _storeId
            });
        }

        NextReminderResult? result =
            await _service.GetNextReminderTypeAsync(1, "6/1/2026", daysLate: 30);

        Assert.Null(result);
    }

    // ── History / Stats ──────────────────────────────────────────────

    [Fact]
    public async Task GetReminderHistoryAsync_ByTicket_ReturnsCorrectItems()
    {
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 10, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "1001", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 20, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "2002", DueDate = "7/1/2026", DaysDiff = 0, StoreId = _storeId
        });

        List<ReminderHistoryItem> history =
            await _service.GetReminderHistoryAsync(ticketKey: 10);

        Assert.Single(history);
        Assert.Equal(10, history[0].TicketKey);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsGroupedByType()
    {
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "1001", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 2, CustomerKey = 2, Phone = "+17185559877",
            TransNo = "2002", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });

        List<ReminderStatistic> stats = await _service.GetStatisticsAsync();

        Assert.Single(stats);
        Assert.Equal("reminder_-7", stats[0].ReminderType);
        Assert.Equal(2, stats[0].Total);
        Assert.Equal(2, stats[0].Successful);
    }

    [Fact]
    public async Task GetRecentRemindersAsync_OrderedByCreatedAtDesc()
    {
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "1001", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 2, CustomerKey = 2, Phone = "+17185559877",
            TransNo = "2002", DueDate = "7/1/2026", DaysDiff = 0, StoreId = _storeId
        });

        List<ReminderHistoryItem> recent = await _service.GetRecentRemindersAsync(limit: 10);

        Assert.Equal(2, recent.Count);
        // Most recent first
        Assert.Equal(2, recent[0].TicketKey);
        Assert.Equal(1, recent[1].TicketKey);
    }

    [Fact]
    public async Task GetSentRemindersAsync_OnlyReturnsSent()
    {
        // Send a valid reminder (status=1)
        await _service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 1, CustomerKey = 1, Phone = "+17185559876",
            TransNo = "1001", DueDate = "6/1/2026", DaysDiff = -7, StoreId = _storeId
        });

        List<SentReminderItem> sent = await _service.GetSentRemindersAsync();

        Assert.Single(sent);
        Assert.Equal("sent", sent[0].Status);
    }

    [Fact]
    public async Task SendReminderAsync_AppendsOnlyToExactPhoneScopedOpenThread()
    {
        const string contactPhone = "+17185559876";
        TwilioNumberEntity defaultNumber = await _db.TwilioNumbers
            .SingleAsync(n => n.StoreId == _storeId);
        TwilioNumberEntity otherNumber = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+13479527213",
            FriendlyName = "Other",
            IsActive = true
        };
        _db.TwilioNumbers.Add(otherNumber);
        await _db.SaveChangesAsync();

        ThreadRepository threadRepo = new(_db);
        Core.Entities.Thread exactThread = await threadRepo.FindOrCreateAsync(
            _storeId, defaultNumber.NumberId, contactPhone, null, null);
        Core.Entities.Thread otherThread = await threadRepo.FindOrCreateAsync(
            _storeId, otherNumber.NumberId, contactPhone, null, null);

        ITwilioService twilioService = new TwilioService(
            Options.Create(new TwilioSettings()), NullLogger<TwilioService>.Instance);
        IStorePhoneResolver phoneResolver = new StorePhoneResolver(new TestStoreRepository(_db));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Reminders:TestMode", "true" }
            })
            .Build();
        ReminderService service = new(
            _db,
            twilioService,
            phoneResolver,
            NullLogger<ReminderService>.Instance,
            configuration: config,
            threadRepo: threadRepo,
            messageRepo: new MessageRepository(_db));

        ReminderSendResult result = await service.SendReminderAsync(new SendReminderRequest
        {
            TicketKey = 9001,
            CustomerKey = 55,
            Phone = contactPhone,
            TransNo = "T-9001",
            DueDate = "6/1/2026",
            DaysDiff = -7,
            StoreId = _storeId
        });

        Assert.True(result.Success);
        MessageEntity message = await _db.Messages.AsNoTracking().SingleAsync();
        Assert.Equal(exactThread.ThreadId, message.ThreadId);
        Assert.NotEqual(otherThread.ThreadId, message.ThreadId);
    }

    // ── Test helpers ─────────────────────────────────────────────────

    // Minimal IStoreRepository implementation for tests.
    private sealed class TestStoreRepository : Core.Repositories.IStoreRepository
    {
        private readonly AppDbContext _db;
        public TestStoreRepository(AppDbContext db) { _db = db; }

        public async Task<Store?> GetByPhoneAsync(string phone, CancellationToken ct = default)
        {
            TwilioNumberEntity? tn = await _db.TwilioNumbers
                .FirstOrDefaultAsync(t => t.PhoneE164 == phone, ct);
            if (tn is null) return null;
            StoreEntity? se = await _db.Stores.FirstOrDefaultAsync(s => s.StoreId == tn.StoreId, ct);
            if (se is null) return null;
            return new Store { StoreId = se.StoreId, StoreName = se.StoreName };
        }

        public async Task<string?> GetDefaultNumberAsync(int storeId, CancellationToken ct = default)
        {
            StoreEntity? store = await _db.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
            if (store is null || store.DefaultNumberId == 0) return null;
            TwilioNumberEntity? tn = await _db.TwilioNumbers
                .FirstOrDefaultAsync(t => t.NumberId == store.DefaultNumberId, ct);
            return tn?.PhoneE164;
        }

        public async Task<TwilioNumber?> GetNumberByPhoneAsync(
            string phoneE164,
            CancellationToken ct = default)
        {
            TwilioNumberEntity? tn = await _db.TwilioNumbers
                .FirstOrDefaultAsync(t => t.PhoneE164 == phoneE164 && t.IsActive, ct);
            return tn is null
                ? null
                : new TwilioNumber
                {
                    NumberId = tn.NumberId,
                    StoreId = tn.StoreId,
                    PhoneE164 = tn.PhoneE164,
                    IsActive = tn.IsActive
                };
        }

        public Task<Store?> GetByIdAsync(int storeId, CancellationToken ct = default)
            => Task.FromResult<Store?>(null);

        public Task<List<Store>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Store>());

        public Task<Store> CreateAsync(string storeName, CancellationToken ct = default)
            => Task.FromResult(new Store { StoreId = 0, StoreName = storeName });
    }
}
