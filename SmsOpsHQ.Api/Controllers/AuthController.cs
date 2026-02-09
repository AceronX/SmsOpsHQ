using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
}
