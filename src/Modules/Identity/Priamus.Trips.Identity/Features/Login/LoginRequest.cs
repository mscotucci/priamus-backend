using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;

namespace Priamus.Trips.Identity.Features.Login;

/// <summary>
/// Request body for <c>POST /api/auth/login</c>.
/// </summary>
[ValidatableType]
public sealed record LoginRequest(
    [Required, MaxLength(64)] string Username,
    [Required, MaxLength(1024)] string Password);
