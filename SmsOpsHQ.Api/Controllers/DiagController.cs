using Microsoft.AspNetCore.Mvc;

namespace SmsOpsHQ.Api.Controllers;

// Diagnostic endpoints for verifying API infrastructure (error handling, health, etc.).
// The error endpoint is restricted to the Development environment.
[ApiController]
[Route("api/diag")]
public class DiagController : ControllerBase
{
    private readonly IHostEnvironment _environment;

    public DiagController(IHostEnvironment environment)
    {
        _environment = environment;
    }

    // Throws a test exception so global exception handling can be verified.
    // Returns 404 outside Development to prevent misuse in production.
    [HttpGet("error")]
    public IActionResult ThrowTestError()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        throw new InvalidOperationException(
            "This is a test exception to verify global error handling.");
    }
}
