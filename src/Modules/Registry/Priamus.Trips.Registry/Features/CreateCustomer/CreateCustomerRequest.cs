using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateCustomer;

/// <summary>
/// Request body for <c>POST /api/registry/customers</c> (PRD §4, RF-15). Only the company name is
/// required; an empty VAT number, tax code or address is stored as none. The company name and the
/// address are measured once trimmed, as the aggregate stores them, so their length is checked in
/// <c>Validate</c> rather than with a length attribute on the raw text.
/// </summary>
[ValidatableType]
public sealed record CreateCustomerRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string CompanyName,
    [VatNumberFormat]
    string? VatNumber,
    [TaxCodeFormat]
    string? TaxCode,
    string? Address) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        CustomerRequestValidation.Validate(CompanyName, Address);
}
