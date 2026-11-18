using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.CreateVehicle;

/// <summary>
/// Request body for <c>POST /api/registry/vehicles</c> (PRD RF-26, RF-27). The description is
/// measured once trimmed, as the aggregate stores it, so its length is checked in
/// <c>Validate</c> rather than with a length attribute on the raw text.
/// </summary>
[ValidatableType]
public sealed record CreateVehicleRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    [LicensePlateFormat]
    string LicensePlate,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    string Description,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    VehicleType? Type,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    bool? RequiresTrailer) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        VehicleRequestValidation.Validate(Description, Type, RequiresTrailer);
}
