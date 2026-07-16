using SmsOpsHQ.Api.HubClient;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class HubLoggingSafetyTests
{
    [Fact]
    public void SafeHostAndPort_RemovesCredentialsPathAndQuery()
    {
        string endpoint = HubEndpointLogFormatter.SafeHostAndPort(
            "https://user:password@hub.example.com:8443/private/path?token=value");

        Assert.Equal("hub.example.com:8443", endpoint);
    }

    [Fact]
    public void RedactSecret_RemovesStoreKeyFromError()
    {
        string message = HubEndpointLogFormatter.RedactSecret(
            "Handshake rejected for store-key-value",
            "store-key-value");

        Assert.Equal("Handshake rejected for [redacted]", message);
    }
}
