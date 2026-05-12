using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.RefreshTokens.Events;

/// <summary>
/// Raised when a refresh token is explicitly revoked (logout, admin revoke).
/// Rotation produces <see cref="RefreshTokenRotated"/> instead — the two paths
/// are deliberately distinct so the audit trail makes intent visible.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the token was revoked.</param>
/// <param name="RefreshTokenId">Identifier of the revoked token.</param>
/// <param name="UserId">Owning user.</param>
public sealed record RefreshTokenRevoked(
    Guid EventId,
    DateTime OccurredAt,
    RefreshTokenId RefreshTokenId,
    UserId UserId) : IDomainEvent;
