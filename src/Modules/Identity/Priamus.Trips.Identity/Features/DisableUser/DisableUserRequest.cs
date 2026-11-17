using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.DisableUser;

/// <summary>
/// Request body for <c>POST /api/identity/users/{id}/disable</c>. <c>Version</c> is the version
/// the client read; the change is refused when the user has changed since.
/// </summary>
[ValidatableType]
public sealed record DisableUserRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    int? Version);
