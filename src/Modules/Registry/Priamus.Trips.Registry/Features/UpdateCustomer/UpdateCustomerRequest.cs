using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.UpdateCustomer;

/// <summary>
/// Request body for <c>PUT /api/registry/customers/{id}</c> (PRD §4, RF-15). <c>Version</c> is the
/// version the client read; the save is refused when the customer has changed since.
/// </summary>
[ValidatableType]
public sealed record UpdateCustomerRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string CompanyName,
    [VatNumberFormat]
    string? VatNumber,
    [TaxCodeFormat]
    string? TaxCode,
    string? Address,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        CustomerRequestValidation.Validate(CompanyName, Address);
}
