using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class PasswordValidatorTests
{
    [Fact]
    public void EmptyPassword_Fails()
    {
        PasswordValidationResult result = PasswordValidator.Validate("");
        Assert.False(result.IsValid);
        Assert.Contains("empty", result.ErrorMessage!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortPassword_Fails()
    {
        PasswordValidationResult result = PasswordValidator.Validate("Abc1");
        Assert.False(result.IsValid);
        Assert.Contains("8 characters", result.ErrorMessage!);
    }

    [Fact]
    public void NoUppercase_Fails()
    {
        PasswordValidationResult result = PasswordValidator.Validate("abcdefg1");
        Assert.False(result.IsValid);
        Assert.Contains("uppercase", result.ErrorMessage!);
    }

    [Fact]
    public void NoLowercase_Fails()
    {
        PasswordValidationResult result = PasswordValidator.Validate("ABCDEFG1");
        Assert.False(result.IsValid);
        Assert.Contains("lowercase", result.ErrorMessage!);
    }

    [Fact]
    public void NoDigit_Fails()
    {
        PasswordValidationResult result = PasswordValidator.Validate("ABCDEFgh");
        Assert.False(result.IsValid);
        Assert.Contains("digit", result.ErrorMessage!);
    }

    [Fact]
    public void ValidPassword_Passes()
    {
        PasswordValidationResult result = PasswordValidator.Validate("SecurePass1");
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExactMinLength_WithComplexity_Passes()
    {
        PasswordValidationResult result = PasswordValidator.Validate("Abcdefg1");
        Assert.True(result.IsValid);
    }
}
