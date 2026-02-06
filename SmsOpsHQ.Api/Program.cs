using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using SmsOpsHQ.Infrastructure;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection in configuration.");

// Infrastructure: DbContext, repositories, auth service, JWT options.
builder.Services.AddInfrastructure(connectionString, builder.Configuration);

// MVC controllers (for AuthController, etc.).
builder.Services.AddControllers();

// Problem Details: RFC 7807 structured error responses for all 4xx/5xx status codes.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = (ProblemDetailsContext problemDetailsContext) =>
    {
        problemDetailsContext.ProblemDetails.Extensions["traceId"] =
            problemDetailsContext.HttpContext.TraceIdentifier;
    };
});

// JWT Bearer authentication.
JwtSettings jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Missing Jwt configuration section.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

// CORS: allow the WPF desktop client (localhost origins) and any dev tools.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

WebApplication app = builder.Build();

// Seed initial data (idempotent: skips if admin already exists).
await SeedData.InitializeAsync(app.Services);

// Global exception handler: unhandled exceptions produce 500 Problem Details (RFC 7807).
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async (HttpContext httpContext) =>
    {
        IExceptionHandlerFeature? exceptionHandlerFeature =
            httpContext.Features.Get<IExceptionHandlerFeature>();
        Exception? error = exceptionHandlerFeature?.Error;

        IProblemDetailsService problemDetailsService =
            httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        IHostEnvironment environment =
            httpContext.RequestServices.GetRequiredService<IHostEnvironment>();

        // In Development, include the exception message for easier debugging.
        // In Production, return a generic message to avoid leaking internal details.
        string detailMessage = environment.IsDevelopment() && error is not null
            ? error.Message
            : "An unexpected error occurred. Please try again later.";

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = detailMessage
            }
        });
    });
});

// Status code pages: converts bare 4xx/5xx responses (e.g. 401 from JWT middleware
// when no token is provided, or 404 for unknown routes) into Problem Details JSON.
app.UseStatusCodePages(async (StatusCodeContext statusCodeContext) =>
{
    HttpContext httpContext = statusCodeContext.HttpContext;
    IProblemDetailsService problemDetailsService =
        httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = httpContext,
        ProblemDetails =
        {
            Status = httpContext.Response.StatusCode
        }
    });
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Ok(new { message = "SmsOps HQ API" }));

app.MapGet("/health", (IHostEnvironment env) => Results.Ok(new
{
    ok = true,
    service = "SmsOps HQ",
    env = env.EnvironmentName
}));

app.Run();

// Make the auto-generated Program class public so integration tests
// can reference it via WebApplicationFactory<Program>.
public partial class Program { }
