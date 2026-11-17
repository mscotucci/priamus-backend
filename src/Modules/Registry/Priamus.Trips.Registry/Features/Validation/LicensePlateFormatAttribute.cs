using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// Validates that a license plate, once normalised (uppercase, spaces and hyphens removed), is
/// 2 to 10 ASCII letters and digits (PRD §6.6, RF-26). It calls the same normalisation as the
/// domain, so the request and the aggregate cannot disagree; the error lands on the plate field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LicensePlateFormatAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Null or blank is reported by [Required]; skip here so the field gets a single message.
        if (value is not string plate || string.IsNullOrWhiteSpace(plate) || LicensePlate.IsValid(plate))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            RegistryMessages.Validation_LicensePlate,
            [validationContext.MemberName ?? string.Empty]);
    }
}
