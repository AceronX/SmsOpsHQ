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
}
