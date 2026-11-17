using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.EnableUser;

/// <summary>
/// Request body for <c>POST /api/identity/users/{id}/enable</c>. <c>Version</c> is the version
/// the client read; the change is refused when the user has changed since.
/// </summary>
[ValidatableType]
public sealed record EnableUserRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    int? Version);
