using System.Text.RegularExpressions;

namespace SmsOpsHQ.Core.Utilities;

// Validates password complexity requirements.
// Rules: min 8 characters, at least one uppercase, one lowercase, one digit.
public static class PasswordValidator
{
    public const int MinLength = 8;

    public static PasswordValidationResult Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordValidationResult.Fail("Password cannot be empty.");

        if (password.Length < MinLength)
            return PasswordValidationResult.Fail($"Password must be at least {MinLength} characters.");

        if (!Regex.IsMatch(password, "[A-Z]"))
            return PasswordValidationResult.Fail("Password must contain at least one uppercase letter.");

        if (!Regex.IsMatch(password, "[a-z]"))
            return PasswordValidationResult.Fail("Password must contain at least one lowercase letter.");

        if (!Regex.IsMatch(password, "[0-9]"))
            return PasswordValidationResult.Fail("Password must contain at least one digit.");

        return PasswordValidationResult.Ok();
    }
}

public sealed class PasswordValidationResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static PasswordValidationResult Ok() => new() { IsValid = true };
    public static PasswordValidationResult Fail(string message) => new() { IsValid = false, ErrorMessage = message };
}
