using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.DeactivateLocation;

/// <summary>
/// Request body for <c>POST /api/registry/locations/{id}/deactivate</c>. <c>Version</c> is the version
/// the client read; the change is refused when the location has changed since.
/// </summary>
[ValidatableType]
public sealed record DeactivateLocationRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version);
