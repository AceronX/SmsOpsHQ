using System.Diagnostics;

namespace SmsOpsHQ.Api.Middleware;

// Logs incoming HTTP requests and outgoing responses with timing information.
// Designed for structured logging with Serilog enrichment.
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string requestMethod = context.Request.Method;
        string requestPath = context.Request.Path;
        string? queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null;
        string? remoteIp = context.Connection.RemoteIpAddress?.ToString();

        _logger.LogDebug(
            "Request: {Method} {Path}{Query} from {RemoteIp}",
            requestMethod, requestPath, queryString ?? "", remoteIp ?? "unknown");

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Unhandled exception: {Method} {Path} in {ElapsedMs}ms",
                requestMethod, requestPath, stopwatch.ElapsedMilliseconds);
            throw; // Re-throw so the exception handler middleware can process it.
        }

        stopwatch.Stop();

        int statusCode = context.Response.StatusCode;
        long elapsedMs = stopwatch.ElapsedMilliseconds;

        if (statusCode >= 500)
        {
            _logger.LogError(
                "Response: {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                requestMethod, requestPath, statusCode, elapsedMs);
        }
        else if (statusCode >= 400)
        {
            _logger.LogWarning(
                "Response: {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                requestMethod, requestPath, statusCode, elapsedMs);
        }
        else
        {
            _logger.LogInformation(
                "Response: {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                requestMethod, requestPath, statusCode, elapsedMs);
        }
    }
}
