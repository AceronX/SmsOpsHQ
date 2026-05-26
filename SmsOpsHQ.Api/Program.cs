using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Infrastructure;
using SmsOpsHQ.Infrastructure.Hubs;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Services;

// Bootstrap Serilog early so startup errors are captured.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/smsops-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Replace default logging with Serilog.
    builder.Host.UseSerilog((HostBuilderContext context, LoggerConfiguration loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/smsops-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30);
    });

    string? sqlitePath = builder.Configuration.GetSection("Database")["SqlitePath"];
    string connectionString = !string.IsNullOrWhiteSpace(sqlitePath)
        ? "Data Source=" + sqlitePath.Trim()
        : builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Set Database:SqlitePath or ConnectionStrings:DefaultConnection in appsettings.");

    // Infrastructure: DbContext, repositories, auth service, JWT options.
    builder.Services.AddInfrastructure(connectionString, builder.Configuration);

    // Merge Twilio settings from Desktop's shared config file (%AppData%\SmsOpsHQ\twilio_config.json).
    // Each property is only filled from the file when not already set in appsettings (so credentials
    // can live in appsettings while MessagingServiceSid lives in the shared JSON, or vice versa).
    builder.Services.PostConfigure<TwilioSettings>(settings =>
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configPath = Path.Combine(appData, "SmsOpsHQ", "twilio_config.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            string json = File.ReadAllText(configPath);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (string.IsNullOrWhiteSpace(settings.AccountSid)
                && doc.RootElement.TryGetProperty("accountSid", out JsonElement sidEl))
                settings.AccountSid = sidEl.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(settings.AuthToken)
                && doc.RootElement.TryGetProperty("authToken", out JsonElement tokenEl))
                settings.AuthToken = tokenEl.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid)
                && doc.RootElement.TryGetProperty("messagingServiceSid", out JsonElement mgEl))
                settings.MessagingServiceSid = mgEl.GetString() ?? string.Empty;

            Log.Information("Merged Twilio settings from {Path} (empty fields only)", configPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load Twilio config from {Path}", configPath);
        }
    });

    // MVC controllers (for AuthController, etc.).
    builder.Services.AddControllers();

    // SignalR: real-time hub for SMS operations updates.
    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    // Swagger / OpenAPI (M47)
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "SmsOps HQ API",
            Version = "v1",
            Description = "SMS operations management API for pawn shop communication workflows."
        });

        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your JWT token (without 'Bearer ' prefix)."
        });

        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

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
    // M46: Support environment variable override for JWT secret (production safety).
    JwtSettings jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException("Missing Jwt configuration section.");

    string jwtSecret = Environment.GetEnvironmentVariable("SMSOPSHQ_JWT_SECRET")
        ?? jwtSettings.Secret;

    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
        throw new InvalidOperationException(
            "JWT secret must be at least 32 characters. Set SMSOPSHQ_JWT_SECRET env var or configure Jwt:Secret.");

    // Propagate the resolved secret back into configuration so AuthService (via IOptions<JwtSettings>)
    // uses the same value. This prevents a mismatch between token signing and validation.
    jwtSettings.Secret = jwtSecret;
    builder.Services.PostConfigure<JwtSettings>(opts => opts.Secret = jwtSecret);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Prevent default claim type mapping (e.g. "role" → long URI form)
            // so our ClaimsPrincipalExtensions can find claims by short names.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

    builder.Services.AddAuthorization();

    // M46: CORS restricted to known origins (desktop client localhost + configurable).
    string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5000", "http://localhost:5173", "https://localhost:5001"];

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
        });
    });

    // M46: Rate limiting on login endpoint (5 attempts per minute per IP).
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });

    WebApplication app = builder.Build();

    // Seed initial data (idempotent: skips if admin already exists).
    await SeedData.InitializeAsync(app.Services);

    // Serilog request logging (replaces default Microsoft request logging).
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
        options.GetLevel = (HttpContext httpContext, double elapsed, Exception? ex) =>
        {
            if (ex is not null) return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 500) return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400) return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
    });

    // Swagger UI (M47)
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SmsOps HQ API v1");
        options.RoutePrefix = "swagger";
    });

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
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // SignalR hub endpoint for real-time SMS updates.
    app.MapHub<SmsOpsHub>("/hubs/smsops");

    app.MapGet("/", () => Results.Ok(new { message = "SmsOps HQ API" }));

    app.MapGet("/health", (IHostEnvironment env) => Results.Ok(new
    {
        ok = true,
        service = "SmsOps HQ",
        env = env.EnvironmentName
    }));

    IReminderScheduler reminderScheduler = app.Services.GetRequiredService<IReminderScheduler>();
    reminderScheduler.Start();

    IReviewAutomationScheduler reviewAutomationScheduler = app.Services.GetRequiredService<IReviewAutomationScheduler>();
    reviewAutomationScheduler.Start();

    // Hourly (configurable) automatic XPD sync. The scheduler internally
    // checks XpdSync:Enabled in config, so calling Start() unconditionally
    // is safe -- it logs and no-ops if disabled. Operators can also flip
    // it on/off at runtime via /api/sync/scheduler/start|stop.
    IXpdSyncScheduler xpdSyncScheduler = app.Services.GetRequiredService<IXpdSyncScheduler>();
    xpdSyncScheduler.Start();

    // Surface Twilio mode at startup so the operator notices immediately if outbound SMS
    // is going to be silently mocked (e.g. credentials not yet configured).
    using (IServiceScope startupScope = app.Services.CreateScope())
    {
        ITwilioService twilio = startupScope.ServiceProvider.GetRequiredService<ITwilioService>();
        if (twilio.IsMockMode)
        {
            Log.Warning("============================================================");
            Log.Warning(" TWILIO MOCK MODE — outbound SMS will NOT be delivered.");
            Log.Warning(" Inbound webhooks still work, but messages sent from the");
            Log.Warning(" app will not reach customers.");
            Log.Warning(" Fix: open the desktop app → Settings → Twilio,");
            Log.Warning(" enter Account SID + Auth Token, click Save.");
            Log.Warning("============================================================");
        }
        else
        {
            Log.Information(
                "Twilio LIVE mode (Account SID prefix: {Prefix}, Messaging Service: {HasMs})",
                twilio.AccountSidPrefix, twilio.HasMessagingService);
        }
    }

    Log.Information("SmsOps HQ API starting...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Make the auto-generated Program class public so integration tests
// can reference it via WebApplicationFactory<Program>.
public partial class Program { }
