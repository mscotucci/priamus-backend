using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// Validates that a tax code, once normalised (uppercase, spaces removed), is 16 letters and digits
/// or 11 digits (PRD §4, RF-15). An empty value is valid: the tax code is optional. It calls the
/// same functions as the domain, so the request and the aggregate cannot disagree; the error lands
/// on the tax code field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class TaxCodeFormatAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string taxCode || string.IsNullOrWhiteSpace(taxCode) || TaxCode.IsValid(taxCode))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            RegistryMessages.Validation_TaxCode,
            [validationContext.MemberName ?? string.Empty]);
    }
}
