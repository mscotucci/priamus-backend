using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateLocation;

/// <summary>
/// Request body for <c>POST /api/registry/locations</c> (PRD §5.1, RF-23). The name and the
/// municipality are required; an empty address is stored as none. All three are measured once
/// trimmed, as the aggregate stores them, so their length is checked in <c>Validate</c> rather than
/// with a length attribute on the raw text.
/// </summary>
[ValidatableType]
public sealed record CreateLocationRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string Name,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string Municipality,
    string? Address) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        LocationRequestValidation.Validate(Name, Municipality, Address);
}
