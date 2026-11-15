using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// Validates that a password meets the application's strength requirements:
/// <list type="bullet">
///   <item>Length between 8 and 128 characters (inclusive).</item>
///   <item>At least one ASCII letter (a-z or A-Z).</item>
///   <item>At least one ASCII digit (0-9).</item>
///   <item>At least one non-alphanumeric printable ASCII character (special character).</item>
/// </list>
/// </summary>
/// <remarks>
/// The error message is in Italian as required by the UI localization convention
/// (docs/context.md §Language). When a .resx localization infrastructure is introduced,
/// the message should be keyed by <c>Validation.StrongPassword</c> and resolved
/// via <c>IStringLocalizer</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed partial class StrongPasswordAttribute : ValidationAttribute
{
    /// <summary>Minimum password length.</summary>
    public const int MinLength = 8;

    /// <summary>Maximum password length.</summary>
    public const int MaxLength = 128;

    [GeneratedRegex(@"[a-zA-Z]", RegexOptions.CultureInvariant)]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"[0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRegex();

    // Non-alphanumeric printable ASCII: characters in the range 0x21-0x7E that are not a-z, A-Z, or 0-9.
    [GeneratedRegex(@"[!-/:-@\[-`{-~]", RegexOptions.CultureInvariant)]
    private static partial Regex SpecialCharRegex();

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string password)
        {
            // Null/missing is handled by [Required]; skip here.
            return ValidationResult.Success;
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            return new ValidationResult(
                GetErrorMessage(),
                [validationContext.MemberName ?? string.Empty]);
        }

        if (!LetterRegex().IsMatch(password))
        {
            return new ValidationResult(
                GetErrorMessage(),
                [validationContext.MemberName ?? string.Empty]);
        }

        if (!DigitRegex().IsMatch(password))
        {
            return new ValidationResult(
                GetErrorMessage(),
                [validationContext.MemberName ?? string.Empty]);
        }

        if (!SpecialCharRegex().IsMatch(password))
        {
            return new ValidationResult(
                GetErrorMessage(),
                [validationContext.MemberName ?? string.Empty]);
        }

        return ValidationResult.Success;
    }

    private static string GetErrorMessage() => IdentityMessages.Validation_StrongPassword;
}
