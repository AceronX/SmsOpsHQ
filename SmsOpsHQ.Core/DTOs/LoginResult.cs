namespace SmsOpsHQ.Core.DTOs;

// Successful login response returned by IAuthService and the login endpoint.
// Matches the POST /api/auth/login 200 response shape.
public sealed class LoginResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public UserDto User { get; set; } = new();
}
