using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for User entities.
public interface IUserRepository
{
    // Find user by login name (case-insensitive). Returns null if not found.
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    // Find user by primary key. Returns null if not found.
    Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);

    // Update the user's last login timestamp.
    Task UpdateLastLoginAtAsync(int userId, DateTime utcNow, CancellationToken cancellationToken = default);

    // Update the user's display name.
    Task UpdateFullNameAsync(int userId, string fullName, CancellationToken cancellationToken = default);

    // Update the user's password hash (caller must hash the password).
    Task UpdatePasswordHashAsync(int userId, string passwordHash, CancellationToken cancellationToken = default);
}
