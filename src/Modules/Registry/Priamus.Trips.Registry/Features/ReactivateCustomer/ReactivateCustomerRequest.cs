using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.ReactivateCustomer;

/// <summary>
/// Request body for <c>POST /api/registry/customers/{id}/reactivate</c>. <c>Version</c> is the version
/// the client read; the change is refused when the customer has changed since.
/// </summary>
[ValidatableType]
public sealed record ReactivateCustomerRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version);
