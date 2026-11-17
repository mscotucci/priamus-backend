using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.DecommissionVehicle;

/// <summary>
/// Request body for <c>POST /api/registry/vehicles/{id}/decommission</c>. <c>Version</c> is the version
/// the client read; the change is refused when the vehicle has changed since.
/// </summary>
[ValidatableType]
public sealed record DecommissionVehicleRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version);
