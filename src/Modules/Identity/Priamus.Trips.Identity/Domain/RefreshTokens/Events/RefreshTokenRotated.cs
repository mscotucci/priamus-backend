using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.RefreshTokens.Events;

/// <summary>
/// Raised when a refresh token is rotated. The rotation atomically revokes the
/// old token and issues a new one — both ids are carried so downstream consumers
/// can correlate the chain (hook for future theft detection per ADR 0005).
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the rotation took place.</param>
/// <param name="OldRefreshTokenId">Identifier of the rotated (now revoked) token.</param>
/// <param name="NewRefreshTokenId">Identifier of the replacement token.</param>
/// <param name="UserId">Owning user (shared between old and new tokens).</param>
public sealed record RefreshTokenRotated(
    Guid EventId,
    DateTime OccurredAt,
    RefreshTokenId OldRefreshTokenId,
    RefreshTokenId NewRefreshTokenId,
    UserId UserId) : IDomainEvent;
