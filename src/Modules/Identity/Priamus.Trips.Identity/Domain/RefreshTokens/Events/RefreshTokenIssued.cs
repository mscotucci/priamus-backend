using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.RefreshTokens.Events;

/// <summary>
/// Raised when a fresh refresh token is issued via <see cref="RefreshToken.Issue"/>.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the token was issued.</param>
/// <param name="RefreshTokenId">Identifier of the newly issued refresh token.</param>
/// <param name="UserId">Owning user.</param>
/// <param name="ExpiresAt">UTC instant at which the token expires.</param>
public sealed record RefreshTokenIssued(
    Guid EventId,
    DateTime OccurredAt,
    RefreshTokenId RefreshTokenId,
    UserId UserId,
    DateTime ExpiresAt) : IDomainEvent;
