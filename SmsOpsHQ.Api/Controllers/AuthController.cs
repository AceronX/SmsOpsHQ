using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        LoginResult? result = await _authService.LoginAsync(request, cancellationToken);

        if (result is null)
        {
            return Problem(
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Invalid username or password.");
        }

        return Ok(result);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        int userId = User.GetUserId();
        if (userId <= 0)
            return Unauthorized();

        bool updated = await _authService.UpdateProfileAsync(userId, request.FullName ?? string.Empty, cancellationToken);
        if (!updated)
            return BadRequest(new { detail = "Display name cannot be empty." });

        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        int userId = User.GetUserId();
        if (userId <= 0)
            return Unauthorized();

        string? error = await _authService.ChangePasswordAsync(
            userId,
            request.OldPassword ?? string.Empty,
            request.NewPassword ?? string.Empty,
            cancellationToken);

        if (error is not null)
            return BadRequest(new { detail = error });

        return NoContent();
    }
}
