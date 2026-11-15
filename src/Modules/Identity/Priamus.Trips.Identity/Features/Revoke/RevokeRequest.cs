using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.Revoke;

/// <summary>
/// Request body for <c>POST /api/identity/auth/revoke</c> (self-service logout).
/// </summary>
[ValidatableType]
public sealed record RevokeRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [MaxLength(64, ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_MaxLength))]
    string RefreshToken);
