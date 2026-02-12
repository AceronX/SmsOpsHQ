using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;

namespace SmsOpsHQ.Infrastructure.Services;

// Authenticates users by verifying BCrypt passwords and issuing JWT tokens.
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly JwtSettings _jwtSettings;

    public AuthService(IUserRepository userRepository, IOptions<JwtSettings> jwtSettings)
    {
        _userRepository = userRepository;
        _jwtSettings = jwtSettings.Value;
        // JWT secret is centrally resolved in Program.cs (env var override or config),
        // then propagated via PostConfigure<JwtSettings> so it arrives here already resolved.
    }

    public async Task<LoginResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return null;

        User? user = await _userRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user is null)
            return null;

        if (!user.IsActive)
            return null;

        bool passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
            return null;

        // M46: Update last login timestamp.
        await _userRepository.UpdateLastLoginAtAsync(user.UserId, DateTime.UtcNow, cancellationToken);

        string token = GenerateJwt(user);

        return new LoginResult
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = _jwtSettings.ExpiresInMinutes * 60,
            User = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                StoreId = user.StoreId,
                StoreName = user.StoreName,
                TwilioNumberId = user.TwilioNumberId,
                Role = user.Role
            }
        };
    }

    private string GenerateJwt(User user)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        SymmetricSecurityKey securityKey = new SymmetricSecurityKey(keyBytes);
        SigningCredentials credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        List<Claim> claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", user.Role),
            new Claim("store_id", user.StoreId?.ToString() ?? "")
        };

        DateTime now = DateTime.UtcNow;
        JwtSecurityToken tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_jwtSettings.ExpiresInMinutes),
            signingCredentials: credentials);

        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(tokenDescriptor);
    }

    /// <summary>Updates the existing user identified by userId (from JWT). Only updates in place; never creates a new user.</summary>
    public async Task<bool> UpdateProfileAsync(int userId, string? username = null, int? storeId = null, int? twilioNumberId = null, CancellationToken cancellationToken = default)
    {
        User? currentUser = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (currentUser is null)
            return false;

        bool updated = false;

        // Update username on the same user (never create a new user)
        if (!string.IsNullOrWhiteSpace(username))
        {
            string newUsername = username.Trim();
            if (newUsername != currentUser.Username)
            {
                User? existingWithName = await _userRepository.GetByUsernameAsync(newUsername, cancellationToken);
                if (existingWithName is not null && existingWithName.UserId != userId)
                    throw new InvalidOperationException("Username already exists.");

                await _userRepository.UpdateUsernameAsync(userId, newUsername, cancellationToken);
                updated = true;
            }
        }

        if (storeId.HasValue)
        {
            await _userRepository.UpdateStoreIdAsync(userId, storeId.Value, cancellationToken);
            updated = true;
        }

        if (twilioNumberId.HasValue)
        {
            await _userRepository.UpdateTwilioNumberIdAsync(userId, twilioNumberId.Value, cancellationToken);
            updated = true;
        }

        return updated;
    }

    public async Task<string?> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldPassword))
            return "Current password is required.";
        if (string.IsNullOrWhiteSpace(newPassword))
            return "New password is required.";

        User? user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return "User not found.";

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return "Current password is incorrect.";

        PasswordValidationResult validation = PasswordValidator.Validate(newPassword);
        if (!validation.IsValid)
            return validation.ErrorMessage ?? "Invalid new password.";

        string newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _userRepository.UpdatePasswordHashAsync(userId, newHash, cancellationToken);
        return null;
    }
}
