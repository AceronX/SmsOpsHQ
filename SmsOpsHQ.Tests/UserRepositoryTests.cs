using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using Xunit;

namespace SmsOpsHQ.Tests;

// Integration tests for UserRepository against an in-memory SQLite database.
public class UserRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UserRepository _userRepository;
    private readonly int _seededUserId;
    private readonly int _seededStoreId;

    public UserRepositoryTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        // Seed a store.
        StoreEntity store = new StoreEntity
        {
            StoreName = "Repo Test Store",
            Address = "100 Repo St",
            City = "TestCity",
            State = "TX",
            Zip = "75001",
            IsActive = true
        };
        _db.Stores.Add(store);
        _db.SaveChanges();
        _seededStoreId = store.StoreId;

        // Seed a user.
        UserEntity user = new UserEntity
        {
            Username = "janedoe",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret"),
            Role = "StoreAdmin",
            StoreId = _seededStoreId,
            TwilioNumberId = null,
            IsActive = true
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _seededUserId = user.UserId;

        _userRepository = new UserRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── GetByUsernameAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByUsernameAsync_ExistingUser_ReturnsMappedDomainUser()
    {
        User? user = await _userRepository.GetByUsernameAsync("janedoe");

        Assert.NotNull(user);
        Assert.Equal(_seededUserId, user.UserId);
        Assert.Equal("janedoe", user.Username);
        Assert.Equal("StoreAdmin", user.Role);
        Assert.Equal(_seededStoreId, user.StoreId);
        Assert.True(user.IsActive);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
    }

    [Fact]
    public async Task GetByUsernameAsync_CaseInsensitive_ReturnsUser()
    {
        User? upperCase = await _userRepository.GetByUsernameAsync("JANEDOE");
        User? mixedCase = await _userRepository.GetByUsernameAsync("JaneDoe");

        Assert.NotNull(upperCase);
        Assert.Equal("janedoe", upperCase.Username);

        Assert.NotNull(mixedCase);
        Assert.Equal("janedoe", mixedCase.Username);
    }

    [Fact]
    public async Task GetByUsernameAsync_NonexistentUser_ReturnsNull()
    {
        User? user = await _userRepository.GetByUsernameAsync("nonexistent");

        Assert.Null(user);
    }

    [Fact]
    public async Task GetByUsernameAsync_EmptyString_ReturnsNull()
    {
        User? user = await _userRepository.GetByUsernameAsync("");

        Assert.Null(user);
    }

    // ── GetByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsMappedDomainUser()
    {
        User? user = await _userRepository.GetByIdAsync(_seededUserId);

        Assert.NotNull(user);
        Assert.Equal(_seededUserId, user.UserId);
        Assert.Equal("janedoe", user.Username);
        Assert.Equal("StoreAdmin", user.Role);
        Assert.Equal(_seededStoreId, user.StoreId);
    }

    [Fact]
    public async Task GetByIdAsync_NonexistentId_ReturnsNull()
    {
        User? user = await _userRepository.GetByIdAsync(99999);

        Assert.Null(user);
    }

    // ── Multiple users ──────────────────────────────────────────────────

    [Fact]
    public async Task GetByUsernameAsync_MultipleUsers_ReturnsCorrectOne()
    {
        // Add a second user.
        _db.Users.Add(new UserEntity
        {
            Username = "johnsmith",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
            Role = "HQViewer",
            StoreId = null,
            TwilioNumberId = null,
            IsActive = true
        });
        _db.SaveChanges();

        User? jane = await _userRepository.GetByUsernameAsync("janedoe");
        User? john = await _userRepository.GetByUsernameAsync("johnsmith");

        Assert.NotNull(jane);
        Assert.Equal("janedoe", jane.Username);

        Assert.NotNull(john);
        Assert.Equal("johnsmith", john.Username);
    }
}
