using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateDriver;

/// <summary>
/// Request body for <c>POST /api/registry/drivers</c> (PRD §6.5, RF-24). First and last name are
/// required; an empty phone number is stored as none and a missing <c>UserId</c> means a driver
/// without an account. Lengths and the phone format are checked in <c>Validate</c>, measured once
/// trimmed as the aggregate stores them.
/// </summary>
[ValidatableType]
public sealed record CreateDriverRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string FirstName,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string LastName,
    string? Phone,
    Guid? UserId) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        DriverRequestValidation.Validate(FirstName, LastName, Phone);
}
