using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class HubConfigurationValidatorTests
{
    [Fact]
    public void Validate_EnabledValidSettings_NormalizesAndClamps()
    {
        HubConfigurationValidationResult result = HubConfigurationValidator.Validate(
            enabled: true,
            url: " https://hub.example.com/ ",
            storeKey: " key-value ",
            deploymentId: " main-counter ",
            intervalSeconds: 1);

        Assert.True(result.IsValid);
        Assert.Equal("https://hub.example.com", result.Url);
        Assert.Equal("key-value", result.StoreKey);
        Assert.Equal("main-counter", result.DeploymentId);
        Assert.Equal(10, result.IntervalSeconds);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hub.example.com")]
    [InlineData("ftp://hub.example.com")]
    [InlineData("http://")]
    public void Validate_EnabledInvalidUrl_ReturnsActionableError(string url)
    {
        HubConfigurationValidationResult result = HubConfigurationValidator.Validate(
            true, url, "key", "deployment", 60);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("absolute http or https", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EnabledMissingKeyAndDeployment_ReturnsBothErrors()
    {
        HubConfigurationValidationResult result = HubConfigurationValidator.Validate(
            true, "https://hub.example.com", " ", null, 60);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Store Key", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Deployment ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Disabled_AllowsEmptyConnectionFields()
    {
        HubConfigurationValidationResult result = HubConfigurationValidator.Validate(
            false, string.Empty, string.Empty, string.Empty, 0);

        Assert.True(result.IsValid);
        Assert.Equal(10, result.IntervalSeconds);
    }
}
