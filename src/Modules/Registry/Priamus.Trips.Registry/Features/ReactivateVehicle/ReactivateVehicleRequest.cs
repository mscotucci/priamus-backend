using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.ReactivateVehicle;

/// <summary>
/// Request body for <c>POST /api/registry/vehicles/{id}/reactivate</c>. <c>Version</c> is the version
/// the client read; the change is refused when the vehicle has changed since.
/// </summary>
[ValidatableType]
public sealed record ReactivateVehicleRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version);
