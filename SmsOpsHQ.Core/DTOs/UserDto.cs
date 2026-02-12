namespace SmsOpsHQ.Core.DTOs;

// User information returned to the client (e.g. inside LoginResult).
// Matches the "user" object in the POST /api/auth/login response.
public sealed class UserDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int? StoreId { get; set; }
    public string? StoreName { get; set; }
    public int? TwilioNumberId { get; set; }
    public string Role { get; set; } = string.Empty;
}
