namespace SmsOpsHQ.Api.HubClient;

internal static class HubEndpointLogFormatter
{
    public static string SafeHostAndPort(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return string.IsNullOrWhiteSpace(url) ? "(unset)" : "(invalid-url)";
        }

        return $"{uri.Host}:{uri.Port}";
    }

    public static string RedactSecret(string? value, string? secret)
    {
        string safe = value ?? string.Empty;
        return string.IsNullOrEmpty(secret)
            ? safe
            : safe.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }
}
