namespace SmsOpsHQ.Core.Utilities;

/// <summary>
/// Validates and normalizes the store-to-Hub settings shared by the desktop
/// configuration writer and the API runtime.
/// </summary>
public static class HubConfigurationValidator
{
    public const int MinimumIntervalSeconds = 10;

    public static HubConfigurationValidationResult Validate(
        bool enabled,
        string? url,
        string? storeKey,
        string? deploymentId,
        int intervalSeconds)
    {
        string normalizedUrl = (url ?? string.Empty).Trim().TrimEnd('/');
        string normalizedStoreKey = (storeKey ?? string.Empty).Trim();
        string normalizedDeploymentId = (deploymentId ?? string.Empty).Trim();
        int normalizedInterval = Math.Max(MinimumIntervalSeconds, intervalSeconds);
        List<string> errors = new();

        if (enabled)
        {
            if (!TryValidateUrl(normalizedUrl))
                errors.Add("Hub URL must be an absolute http or https URL.");
            if (string.IsNullOrWhiteSpace(normalizedStoreKey))
                errors.Add("Store Key is required when Hub reporting is enabled.");
            if (string.IsNullOrWhiteSpace(normalizedDeploymentId))
                errors.Add("Deployment ID is required when Hub reporting is enabled.");
        }

        return new HubConfigurationValidationResult(
            errors.Count == 0,
            normalizedUrl,
            normalizedStoreKey,
            normalizedDeploymentId,
            normalizedInterval,
            errors);
    }

    public static bool TryValidateUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrWhiteSpace(uri.Host);
    }
}

public sealed record HubConfigurationValidationResult(
    bool IsValid,
    string Url,
    string StoreKey,
    string DeploymentId,
    int IntervalSeconds,
    IReadOnlyList<string> Errors);
