using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.DeactivateDriver;

/// <summary>
/// Request body for <c>POST /api/registry/drivers/{id}/deactivate</c>. <c>Version</c> is the version
/// the client read; the change is refused when the driver has changed since.
/// </summary>
[ValidatableType]
public sealed record DeactivateDriverRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version);
