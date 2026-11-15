using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.Refresh;

/// <summary>
/// Request body for <c>POST /api/identity/auth/refresh</c>.
/// </summary>
[ValidatableType]
public sealed record RefreshRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [MaxLength(64, ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_MaxLength))]
    string RefreshToken);
