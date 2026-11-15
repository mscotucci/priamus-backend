using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Resources;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// Request body for <c>POST /api/identity/users</c>.
/// </summary>
[ValidatableType]
public sealed record CreateUserRequest(
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [MaxLength(64, ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_MaxLength))]
    string Username,
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    [StrongPassword]
    string Password,
    [Required(ErrorMessageResourceType = typeof(IdentityMessages), ErrorMessageResourceName = nameof(IdentityMessages.Validation_Required))]
    UserRole? Role);
