using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Identity.Domain.Users;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// Request body for <c>POST /api/users</c>.
/// </summary>
[ValidatableType]
public sealed record CreateUserRequest(
    [Required, MaxLength(64)] string Username,
    [Required, StrongPassword] string Password,
    [Required] UserRole Role);
