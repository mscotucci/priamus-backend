using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// Validates that a VAT number, once normalised (spaces and a leading "IT" removed), is 11 digits
/// with a valid Italian check digit (PRD §4, RF-15). An empty value is valid: a customer may have
/// no VAT number. It calls the same functions as the domain, so the request and the aggregate
/// cannot disagree; the error lands on the VAT number field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class VatNumberFormatAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string vatNumber || string.IsNullOrWhiteSpace(vatNumber) || VatNumber.IsValid(vatNumber))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            RegistryMessages.Validation_VatNumber,
            [validationContext.MemberName ?? string.Empty]);
    }
}
