using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Authentication contract. Implemented in Infrastructure (Milestone 7).
public interface IAuthService
{
    // Validates credentials and returns a JWT + user info on success.
    // Returns null if the username does not exist or the password is wrong.
    Task<LoginResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    // Updates username, store ID and/or Twilio number ID for the given user. Returns true if updated.
    Task<bool> UpdateProfileAsync(int userId, string? username = null, int? storeId = null, int? twilioNumberId = null, CancellationToken cancellationToken = default);

    // Changes password for the given user. Verifies old password, hashes and saves new one. Returns error message or null on success.
    Task<string?> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
}
