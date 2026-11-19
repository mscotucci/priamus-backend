using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.UpdateDriver;

/// <summary>
/// Request body for <c>PUT /api/registry/drivers/{id}</c> (PRD §6.5, RF-24). <c>Version</c> is the
/// version the client read; the save is refused when the driver has changed since. A missing
/// <c>UserId</c> removes the account link.
/// </summary>
[ValidatableType]
public sealed record UpdateDriverRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string FirstName,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string LastName,
    string? Phone,
    Guid? UserId,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        DriverRequestValidation.Validate(FirstName, LastName, Phone);
}
