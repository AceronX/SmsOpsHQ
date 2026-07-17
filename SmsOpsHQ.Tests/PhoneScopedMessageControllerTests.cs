using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmsOpsHQ.Api.Controllers;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Models;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class PhoneScopedMessageControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MessagesController _messagesController;
    private readonly ThreadsController _threadsController;
    private readonly int _storeId;
    private readonly int _primaryNumberId;
    private readonly int _otherNumberId;

    public PhoneScopedMessageControllerTests()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        StoreEntity store = new() { StoreName = "Phone Scoped Store", IsActive = true };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _storeId = store.StoreId;

        TwilioNumberEntity primary = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15559990101",
            FriendlyName = "Primary",
            IsActive = true
        };
        TwilioNumberEntity other = new()
        {
            StoreId = _storeId,
            PhoneE164 = "+15559990102",
            FriendlyName = "Other",
            IsActive = true
        };
        _db.TwilioNumbers.AddRange(primary, other);
        _db.SaveChanges();
        _primaryNumberId = primary.NumberId;
        _otherNumberId = other.NumberId;
        store.DefaultNumberId = _primaryNumberId;

        UserEntity user = new()
        {
            StoreId = _storeId,
            Username = "phase3-user",
            PasswordHash = "not-used",
            Role = "StoreAdmin",
            IsActive = true
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        MessageRepository messageRepo = new(_db);
        ThreadRepository threadRepo = new(_db);
        CustomerRepository customerRepo = new(_db);
        StoreRepository storeRepo = new(_db);
        ClaimsPrincipal principal = BuildPrincipal(_storeId, user.UserId);

        _messagesController = new MessagesController(
            messageRepo,
            threadRepo,
            customerRepo,
            new OptOutRepository(_db),
            new OutboundNumberResolver(_db),
            new SameIdentityResolver(),
            new AcceptedTwilioService(),
            new NoOpRealtimeService(),
            NullLogger<MessagesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };

        _threadsController = new ThreadsController(
            threadRepo,
            messageRepo,
            customerRepo,
            storeRepo,
            NullLogger<ThreadsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task Send_SeparatesPhones_AndRejectsThreadDestinationOrSenderMismatch()
    {
        Assert.IsType<OkObjectResult>(await SendAsync("+15552220001", "phone A"));
        Assert.IsType<OkObjectResult>(await SendAsync("+15552220002", "phone B"));

        List<ThreadEntity> threads = await _db.Threads.AsNoTracking()
            .OrderBy(t => t.ContactPhoneE164)
            .ToListAsync();
        Assert.Equal(2, threads.Count);
        Assert.All(threads, thread => Assert.Equal(42, thread.IdentityId));
        Assert.Equal("+15552220001", threads[0].ContactPhoneE164);
        Assert.Equal("+15552220002", threads[1].ContactPhoneE164);

        int phoneAThreadId = threads[0].ThreadId;
        ObjectResult wrongDestination = Assert.IsType<ObjectResult>(
            await SendAsync(
                "+15552220002", "must not append", phoneAThreadId, _primaryNumberId));
        Assert.Equal(StatusCodes.Status409Conflict, wrongDestination.StatusCode);

        ObjectResult wrongSender = Assert.IsType<ObjectResult>(
            await SendAsync(
                "+15552220001", "must not append", phoneAThreadId, _otherNumberId));
        Assert.Equal(StatusCodes.Status409Conflict, wrongSender.StatusCode);
        Assert.Equal(2, await _db.Messages.CountAsync());

        OkObjectResult inboxResult = Assert.IsType<OkObjectResult>(
            await _threadsController.GetInbox(
                _storeId, "open", null, _primaryNumberId, CancellationToken.None));
        using JsonDocument inboxJson = JsonDocument.Parse(JsonSerializer.Serialize(inboxResult.Value));
        string[] contacts = inboxJson.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("contact_phone").GetString()!)
            .OrderBy(phone => phone)
            .ToArray();
        Assert.Equal(new[] { "+15552220001", "+15552220002" }, contacts);

        OkObjectResult detailResult = Assert.IsType<OkObjectResult>(
            await _threadsController.GetThreadDetails(
                phoneAThreadId, _storeId, false, CancellationToken.None));
        using JsonDocument detailJson = JsonDocument.Parse(JsonSerializer.Serialize(detailResult.Value));
        Assert.Equal(
            "+15552220001",
            detailJson.RootElement.GetProperty("thread").GetProperty("contact_phone").GetString());
    }

    private Task<IActionResult> SendAsync(
        string phone,
        string body,
        int? threadId = null,
        int? numberId = null)
    {
        return _messagesController.SendMessage(new SendMessageRequest
        {
            StoreId = _storeId,
            ToPhone = phone,
            Body = body,
            ThreadId = threadId,
            TwilioNumberId = numberId ?? _primaryNumberId
        }, CancellationToken.None);
    }

    private static ClaimsPrincipal BuildPrincipal(int storeId, int userId)
    {
        ClaimsIdentity identity = new(new[]
        {
            new Claim("store_id", storeId.ToString()),
            new Claim("role", "StoreAdmin"),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "test");
        return new ClaimsPrincipal(identity);
    }

    private sealed class SameIdentityResolver : IIdentityResolver
    {
        public Task<List<int>> ResolveCustomerKeysAsync(
            string phoneE164,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<int> { 42 });

        public Task<List<CustomerPhoneMatch>> ResolveCustomerPhoneMatchesAsync(
            string phoneE164,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<CustomerPhoneMatch>());

        public Task<int?> ResolveIdentityIdAsync(
            int storeId,
            string phoneE164,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(42);
    }

    private sealed class AcceptedTwilioService : ITwilioService
    {
        private int _sid;
        public bool IsMockMode => false;
        public string AccountSidPrefix => "ACtest";
        public bool HasMessagingService => false;

        public Task<TwilioSendResult> SendSmsAsync(
            string fromE164,
            string toE164,
            string body,
            List<string>? mediaUrls = null,
            string? statusCallbackUrl = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TwilioSendResult
            {
                Success = true,
                TwilioSid = $"SM_PHASE3_{Interlocked.Increment(ref _sid)}",
                Status = "queued"
            });
        }
    }

    private sealed class NoOpRealtimeService : IRealtimeService
    {
        public Task PushMessageNewAsync(
            int storeId, int threadId, MessageDto message, ThreadDto thread,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PushMessageStatusAsync(
            int storeId, int threadId, int messageId, string twilioSid, string status,
            string? errorCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PushSystemAlertAsync(
            int storeId, string code, string message, string severity,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
