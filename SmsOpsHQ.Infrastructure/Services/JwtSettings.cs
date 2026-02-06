namespace SmsOpsHQ.Infrastructure.Services;

// Configuration values for JWT token generation, bound from appsettings "Jwt" section.
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SmsOpsHQ";
    public string Audience { get; set; } = "SmsOpsHQ";
    public int ExpiresInMinutes { get; set; } = 60;
}
