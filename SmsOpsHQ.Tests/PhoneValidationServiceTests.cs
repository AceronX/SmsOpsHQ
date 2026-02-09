using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Infrastructure.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

// Tests for PhoneValidationService: metadata and body validation.
public class PhoneValidationServiceTests
{
    private readonly PhoneValidationService _service = new();
    private const string StorePhone = "+15551234567";
    private const string CustomerPhone = "+15559876543";

    // ── Metadata validation ──────────────────────────────────────────

    [Fact]
    public void ValidateMessage_ToMatchesStore_IsValid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "Hello",
            storePhone: StorePhone,
            storeId: 1);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
        Assert.False(result.ShouldQuarantine);
    }

    [Fact]
    public void ValidateMessage_FromMatchesStore_IsValid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: CustomerPhone,
            fromPhone: StorePhone,
            body: "Hello",
            storePhone: StorePhone,
            storeId: 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_NeitherMatchesStore_Invalid_ShouldQuarantine()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: "+15550000000",
            fromPhone: CustomerPhone,
            body: "Hello",
            storePhone: StorePhone,
            storeId: 1);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Neither To", result.FailureReason);
        Assert.True(result.ShouldQuarantine);
    }

    [Fact]
    public void ValidateMessage_InvalidStorePhone_Invalid_ShouldQuarantine()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "Hello",
            storePhone: "123", // Too short
            storeId: 1);

        Assert.False(result.IsValid);
        Assert.Contains("invalid", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.ShouldQuarantine);
    }

    // ── Body validation (store phone leakage) ────────────────────────

    [Fact]
    public void ValidateMessage_BodyContainsStorePhone_Invalid_ShouldQuarantine()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "Please call me at 555-123-4567 for details",
            storePhone: StorePhone,
            storeId: 1);

        Assert.False(result.IsValid);
        Assert.Contains("store phone number", result.FailureReason!);
        Assert.True(result.ShouldQuarantine);
    }

    [Fact]
    public void ValidateMessage_BodyContainsStorePhoneFormatted_Invalid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "Our number is (555) 123-4567",
            storePhone: StorePhone,
            storeId: 1);

        Assert.False(result.IsValid);
        Assert.True(result.ShouldQuarantine);
    }

    [Fact]
    public void ValidateMessage_BodyContainsDifferentPhone_IsValid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "My number is 555-000-1111",
            storePhone: StorePhone,
            storeId: 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_EmptyBody_IsValid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "",
            storePhone: StorePhone,
            storeId: 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_NullishBody_IsValid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "   ",
            storePhone: StorePhone,
            storeId: 1);

        Assert.True(result.IsValid);
    }

    // ── Phone format matching ────────────────────────────────────────

    [Fact]
    public void ValidateMessage_PhoneFormatsMatch_DifferentFormats()
    {
        // Store phone as raw digits, To as E.164
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: "5551234567",
            fromPhone: CustomerPhone,
            body: "Hello",
            storePhone: "+15551234567",
            storeId: 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_BodyContainsStorePhoneE164_Invalid()
    {
        PhoneValidationResult result = _service.ValidateMessage(
            toPhone: StorePhone,
            fromPhone: CustomerPhone,
            body: "Text me at +15551234567",
            storePhone: StorePhone,
            storeId: 1);

        Assert.False(result.IsValid);
        Assert.True(result.ShouldQuarantine);
    }
}
