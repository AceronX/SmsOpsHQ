using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Core.Services;

// Authentication contract. Implemented in Infrastructure (Milestone 7).
public interface IAuthService
{
    // Validates credentials and returns a JWT + user info on success.
    // Returns null if the username does not exist or the password is wrong.
    Task<LoginResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
