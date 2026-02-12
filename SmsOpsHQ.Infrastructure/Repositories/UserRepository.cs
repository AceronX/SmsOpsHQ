using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of IUserRepository. Queries UserEntity and maps to domain User.
public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users
            .AsNoTracking()
            .Include(u => u.Store)
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == username.ToLower(),
                cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.UserId == userId,
                cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task UpdateLastLoginAtAsync(int userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (entity is not null)
        {
            entity.LastLoginAt = utcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateStoreIdAsync(int userId, int? storeId, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (entity is null)
            return;

        entity.StoreId = storeId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTwilioNumberIdAsync(int userId, int? twilioNumberId, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (entity is null)
            return;
        
        entity.TwilioNumberId = twilioNumberId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Updates the username of the existing user by primary key. Never creates a new user.</summary>
    public async Task UpdateUsernameAsync(int userId, string username, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (entity is null)
            return;

        entity.Username = username.Trim();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdatePasswordHashAsync(int userId, string passwordHash, CancellationToken cancellationToken = default)
    {
        UserEntity? entity = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (entity is not null)
        {
            entity.PasswordHash = passwordHash;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static User MapToDomain(UserEntity entity)
    {
        return new User
        {
            UserId = entity.UserId,
            StoreId = entity.StoreId,
            StoreName = entity.Store?.StoreName,
            TwilioNumberId = entity.TwilioNumberId,
            Username = entity.Username,
            PasswordHash = entity.PasswordHash,
            Role = entity.Role,
            IsActive = entity.IsActive,
            LastLoginAt = entity.LastLoginAt,
            CreatedAt = entity.CreatedAt
        };
    }
}
