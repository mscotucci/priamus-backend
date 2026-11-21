using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// Validates that a time of day is written <c>HH:mm</c>, as a time input sends it; the error
/// lands on the time field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class TimeOfDayFormatAttribute : ValidationAttribute
{
    /// <summary>Reads an <c>HH:mm</c> text; <c>null</c> when it is not one.</summary>
    internal static TimeOnly? Parse(string? text) =>
        TimeOnly.TryParseExact(text, TimeOfDayText.Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Null or blank is reported by [Required]; skip here so the field gets a single message.
        if (value is not string text || string.IsNullOrWhiteSpace(text) || Parse(text) is not null)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            RegistryMessages.Validation_TimeOfDay,
            [validationContext.MemberName ?? string.Empty]);
    }
}
