using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;

namespace Priamus.Trips.Identity.Features.Refresh;

/// <summary>
/// Request body for <c>POST /api/auth/refresh</c>.
/// </summary>
[ValidatableType]
public sealed record RefreshRequest(
    [Required, MaxLength(64)] string RefreshToken);
