using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class PhoneUtilsTests
{
    // ── NormalizeToE164 ────────────────────────────────────────────────

    [Theory]
    [InlineData("7185551234", "+17185551234")]
    [InlineData("17185551234", "+17185551234")]
    [InlineData("+17185551234", "+17185551234")]
    [InlineData("(718) 555-1234", "+17185551234")]
    [InlineData("718-555-1234", "+17185551234")]
    [InlineData("1-718-555-1234", "+17185551234")]
    [InlineData("1 (718) 555-1234", "+17185551234")]
    [InlineData("  718.555.1234  ", "+17185551234")]
    public void NormalizeToE164_ValidInputs_ReturnsE164(string input, string expected)
    {
        string? result = PhoneUtils.NormalizeToE164(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]         // too few digits
    [InlineData("123456789")]     // 9 digits
    [InlineData("271855512345")]  // 12 digits, doesn't start with 1
    [InlineData("abc")]
    public void NormalizeToE164_InvalidInputs_ReturnsNull(string? input)
    {
        string? result = PhoneUtils.NormalizeToE164(input);
        Assert.Null(result);
    }

    // ── ExtractLast10Digits ────────────────────────────────────────────

    [Theory]
    [InlineData("+17185551234", "7185551234")]
    [InlineData("17185551234", "7185551234")]
    [InlineData("7185551234", "7185551234")]
    [InlineData("(718) 555-1234", "7185551234")]
    [InlineData("1-718-555-1234", "7185551234")]
    public void ExtractLast10Digits_ValidInputs_ReturnsLast10(string input, string expected)
    {
        string? result = PhoneUtils.ExtractLast10Digits(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]     // too few digits
    [InlineData("555-1234")]  // 7 digits
    public void ExtractLast10Digits_InvalidInputs_ReturnsNull(string? input)
    {
        string? result = PhoneUtils.ExtractLast10Digits(input);
        Assert.Null(result);
    }

    // ── IsValidUsPhone ─────────────────────────────────────────────────

    [Theory]
    [InlineData("7185551234", true)]
    [InlineData("+17185551234", true)]
    [InlineData("12345", false)]
    [InlineData(null, false)]
    public void IsValidUsPhone_ReturnsExpected(string? input, bool expected)
    {
        bool result = PhoneUtils.IsValidUsPhone(input);
        Assert.Equal(expected, result);
    }
}
