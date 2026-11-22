using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.UpdateLocation;

/// <summary>
/// Request body for <c>PUT /api/registry/locations/{id}</c> (PRD §5.1, RF-23). <c>Version</c> is the
/// version the client read; the save is refused when the location has changed since.
/// </summary>
[ValidatableType]
public sealed record UpdateLocationRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string Name,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string Municipality,
    string? Address,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        LocationRequestValidation.Validate(Name, Municipality, Address);
}
