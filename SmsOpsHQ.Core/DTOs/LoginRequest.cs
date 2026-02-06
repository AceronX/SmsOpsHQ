namespace SmsOpsHQ.Core.DTOs;

// Credentials sent by the client to POST /api/auth/login.
public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
