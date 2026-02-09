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
    public async Task SendSmsAsync_MockMode_ReturnsSuccess()
    {
        TwilioSendResult result = await _service.SendSmsAsync(
            fromE164: "+15551234567",
            toE164: "+15559876543",
            body: "Hello from mock mode");

        Assert.True(result.Success);
        Assert.NotNull(result.TwilioSid);
        Assert.StartsWith("SM_MOCK_", result.TwilioSid);
        Assert.Equal("Sent", result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
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
