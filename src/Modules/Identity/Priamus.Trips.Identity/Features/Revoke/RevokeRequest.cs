using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;

namespace Priamus.Trips.Identity.Features.Revoke;

/// <summary>
/// Request body for <c>POST /api/auth/revoke</c> (self-service logout).
/// </summary>
[ValidatableType]
public sealed record RevokeRequest(
    [Required, MaxLength(64)] string RefreshToken);
