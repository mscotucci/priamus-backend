using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.Login;

/// <summary>
/// Request body for <c>POST /api/identity/auth/login</c>.
/// </summary>
[ValidatableType]
public sealed record LoginRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [MaxLength(64, ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_MaxLength))]
    string Username,
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [MaxLength(1024, ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_MaxLength))]
    string Password);
