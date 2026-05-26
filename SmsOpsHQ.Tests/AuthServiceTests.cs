using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly JwtSettings _jwtSettings;

    public AuthServiceTests()
    {
        DbContextOptions<AppDbContext> dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.Migrate();

        // Seed a store for StoreId FK tests.
        StoreEntity testStore = new StoreEntity
        {
            StoreName = "Test Store",
            Address = "1 Test Rd",
            City = "TestCity",
            State = "NY",
            Zip = "10001",
            IsActive = true
        };
        _db.Stores.Add(testStore);
        _db.SaveChanges();

        // Seed an HQ admin user (no StoreId).
        string adminHash = BCrypt.Net.BCrypt.HashPassword("password");
        _db.Users.Add(new UserEntity
        {
            Username = "admin",
            PasswordHash = adminHash,
            Role = "HQAdmin",
            StoreId = null,
            TwilioNumberId = null,
            IsActive = true
        });

        // Seed a store-level user (with StoreId).
        string storeUserHash = BCrypt.Net.BCrypt.HashPassword("storepass");
        _db.Users.Add(new UserEntity
        {
            Username = "storemanager",
            PasswordHash = storeUserHash,
            Role = "StoreManager",
            StoreId = testStore.StoreId,
            TwilioNumberId = null,
            IsActive = true
        });

        // Seed an inactive user.
        string inactiveHash = BCrypt.Net.BCrypt.HashPassword("inactive");
        _db.Users.Add(new UserEntity
        {
            Username = "inactive",
            PasswordHash = inactiveHash,
            Role = "HQViewer",
            StoreId = null,
            TwilioNumberId = null,
            IsActive = false
        });

        _db.SaveChanges();

        IUserRepository userRepository = new UserRepository(_db);

        _jwtSettings = new JwtSettings
        {
            Secret = "TestSecretKeyThatIsAtLeast32CharsLong!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiresInMinutes = 60
        };

        _authService = new AuthService(userRepository, Options.Create(_jwtSettings));
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── Valid credentials ────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsLoginResult()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(3600, result.ExpiresIn);
        Assert.Equal("admin", result.User.Username);
        Assert.Equal("HQAdmin", result.User.Role);
        Assert.Null(result.User.StoreId);
    }

    [Fact]
    public async Task LoginAsync_StoreUser_ReturnsCorrectStoreId()
    {
        LoginRequest request = new LoginRequest { Username = "storemanager", Password = "storepass" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.NotNull(result);
        Assert.Equal("storemanager", result.User.Username);
        Assert.Equal("StoreManager", result.User.Role);
        Assert.NotNull(result.User.StoreId);
        Assert.True(result.User.StoreId > 0);
    }

    // ── Invalid credentials ─────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "wrongpassword" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_NonexistentUser_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "nobody", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_EmptyUsername_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_EmptyPassword_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_WhitespaceOnlyUsername_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "   ", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ReturnsNull()
    {
        LoginRequest request = new LoginRequest { Username = "inactive", Password = "inactive" };

        LoginResult? result = await _authService.LoginAsync(request);

        Assert.Null(result);
    }

    // ── JWT token validation ────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_JwtContainsExpectedClaims()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);
        Assert.NotNull(result);

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(result.AccessToken);

        string? subClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        string? uniqueNameClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value;
        string? roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        string? jtiClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

        Assert.NotNull(subClaim);
        Assert.Equal("admin", uniqueNameClaim);
        Assert.Equal("HQAdmin", roleClaim);
        Assert.False(string.IsNullOrEmpty(jtiClaim));
    }

    [Fact]
    public async Task LoginAsync_StoreUser_JwtContainsStoreIdClaim()
    {
        LoginRequest request = new LoginRequest { Username = "storemanager", Password = "storepass" };

        LoginResult? result = await _authService.LoginAsync(request);
        Assert.NotNull(result);

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(result.AccessToken);

        string? storeIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "store_id")?.Value;
        Assert.NotNull(storeIdClaim);
        Assert.False(string.IsNullOrEmpty(storeIdClaim));
        Assert.True(int.TryParse(storeIdClaim, out int parsedStoreId));
        Assert.True(parsedStoreId > 0);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_JwtHasCorrectIssuerAndAudience()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);
        Assert.NotNull(result);

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(result.AccessToken);

        Assert.Equal(_jwtSettings.Issuer, jwt.Issuer);
        Assert.Contains(_jwtSettings.Audience, jwt.Audiences);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_JwtExpiresInConfiguredMinutes()
    {
        LoginRequest request = new LoginRequest { Username = "admin", Password = "password" };

        LoginResult? result = await _authService.LoginAsync(request);
        Assert.NotNull(result);

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt = tokenHandler.ReadJwtToken(result.AccessToken);

        Assert.NotEqual(default, jwt.ValidTo);
        Assert.NotEqual(default, jwt.ValidFrom);

        TimeSpan tokenLifetime = jwt.ValidTo - jwt.ValidFrom;
        double expectedMinutes = _jwtSettings.ExpiresInMinutes;

        // Allow a small tolerance for test execution time.
        Assert.InRange(tokenLifetime.TotalMinutes, expectedMinutes - 1, expectedMinutes + 1);
    }
}
