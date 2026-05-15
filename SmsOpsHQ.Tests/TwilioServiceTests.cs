using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for TwilioService operating in mock mode (no real credentials).
public class TwilioServiceTests
{
    private readonly TwilioService _service;

    public TwilioServiceTests()
    {
        // Empty AccountSid/AuthToken = mock mode.
        TwilioSettings settings = new TwilioSettings
        {
            AccountSid = "",
            AuthToken = ""
        };

        _service = new TwilioService(
            Options.Create(settings),
            NullLogger<TwilioService>.Instance);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_ReturnsSuccessWithMockFlag()
    {
        TwilioSendResult result = await _service.SendSmsAsync(
            fromE164: "+15551234567",
            toE164: "+15559876543",
            body: "Hello from mock mode");

        // We deliberately keep Success = true so reminder/review pipelines that branch
        // on Success continue to work in unit tests, but we surface IsMock = true and
        // a non-"Sent" status so the API and UI can flag undelivered messages clearly.
        Assert.True(result.Success);
        Assert.True(result.IsMock);
        Assert.NotNull(result.TwilioSid);
        Assert.StartsWith("SM_MOCK_", result.TwilioSid);
        Assert.Equal("Mock", result.Status);
        Assert.Equal("MOCK_MODE", result.ErrorCode);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public void Service_ReportsMockMode_WhenCredentialsMissing()
    {
        Assert.True(_service.IsMockMode);
        Assert.Equal(string.Empty, _service.AccountSidPrefix);
        Assert.False(_service.HasMessagingService);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_ReturnsDifferentSidsEachCall()
    {
        TwilioSendResult result1 = await _service.SendSmsAsync(
            "+15551234567", "+15559876543", "Message 1");
        TwilioSendResult result2 = await _service.SendSmsAsync(
            "+15551234567", "+15559876543", "Message 2");

        Assert.NotEqual(result1.TwilioSid, result2.TwilioSid);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_WithMediaUrls_ReturnsSuccess()
    {
        TwilioSendResult result = await _service.SendSmsAsync(
            fromE164: "+15551234567",
            toE164: "+15559876543",
            body: "Photo message",
            mediaUrls: new List<string> { "https://example.com/image.jpg" });

        Assert.True(result.Success);
        Assert.NotNull(result.TwilioSid);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_WithStatusCallback_ReturnsSuccess()
    {
        TwilioSendResult result = await _service.SendSmsAsync(
            fromE164: "+15551234567",
            toE164: "+15559876543",
            body: "Callback message",
            statusCallbackUrl: "https://example.com/callback");

        Assert.True(result.Success);
        Assert.NotNull(result.TwilioSid);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_SidLength_Is34Characters()
    {
        TwilioSendResult result = await _service.SendSmsAsync(
            "+15551234567", "+15559876543", "Length test");

        Assert.NotNull(result.TwilioSid);
        Assert.Equal(34, result.TwilioSid.Length);
    }

    [Fact]
    public async Task SendSmsAsync_MockMode_LongBody_ReturnsSuccess()
    {
        string longBody = new string('x', 1600); // SMS segment limit

        TwilioSendResult result = await _service.SendSmsAsync(
            "+15551234567", "+15559876543", longBody);

        Assert.True(result.Success);
    }
}
