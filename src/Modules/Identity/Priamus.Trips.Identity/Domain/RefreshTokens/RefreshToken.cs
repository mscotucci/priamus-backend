using Priamus.Trips.Identity.Domain.RefreshTokens.Events;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Domain.RefreshTokens;

/// <summary>
/// Refresh-token aggregate (ADR 0005). One row per issued refresh token. Distinct
/// aggregate from <see cref="Users.User"/> so the back office can manage individual
/// sessions (revoke a single device) without loading the full user.
/// </summary>
/// <remarks>
/// <para>
/// Cross-aggregate references are bare typed IDs: <see cref="UserId"/> is a value
/// only, no navigation property, no FK at the DB level (modules own their schema).
/// </para>
/// <para>
/// The aggregate never sees the cleartext refresh token: it stores only a hash
/// (see <see cref="RefreshTokenHash"/>) computed by the application layer with SHA-256.
/// </para>
/// </remarks>
public sealed class RefreshToken : AggregateRoot
{
    /// <summary>EF only.</summary>
    private RefreshToken()
    {
        TokenHash = null!;
    }

    private RefreshToken(
        RefreshTokenId id,
        UserId userId,
        RefreshTokenHash tokenHash,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = null;
        ReplacedByTokenId = null;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public RefreshTokenId Id { get; private set; }

    /// <summary>Owning user. Bare reference — no navigation property.</summary>
    public UserId UserId { get; private set; }

    /// <summary>SHA-256 hash of the opaque refresh-token string.</summary>
    public RefreshTokenHash TokenHash { get; private set; }

    /// <summary>UTC instant at which the token was issued.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC instant at which the token expires (computed from the configured lifetime).</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// UTC instant at which the token was revoked, either explicitly or as a side
    /// effect of rotation. <c>null</c> while still revocable.
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Identifier of the replacement token issued by <see cref="Rotate"/>.
    /// Non-null implies the token has been rotated (and therefore also revoked).
    /// </summary>
    public RefreshTokenId? ReplacedByTokenId { get; private set; }

    /// <summary>True iff <see cref="ReplacedByTokenId"/> is set.</summary>
    public bool IsRotated => ReplacedByTokenId.HasValue;

    /// <summary>
    /// True iff the token is currently usable for a refresh: not revoked, not
    /// expired (per the provided clock), not yet rotated.
    /// </summary>
    /// <param name="clock">Time source for the freshness check.</param>
    public bool IsActive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return RevokedAt is null
            && !IsRotated
            && clock.UtcNow < ExpiresAt;
    }

    /// <summary>
    /// Issues a fresh refresh token for <paramref name="userId"/>.
    /// </summary>
    /// <param name="id">Identifier of the new token.</param>
    /// <param name="userId">Owning user.</param>
    /// <param name="hash">SHA-256 hash of the opaque token.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="lifetime">Strictly-positive lifetime; <c>ExpiresAt = clock.UtcNow + lifetime</c>.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="RefreshTokenErrors.LifetimeInvalid"/> when <paramref name="lifetime"/>
    /// is zero or negative.
    /// </exception>
    public static RefreshToken Issue(
        RefreshTokenId id,
        UserId userId,
        RefreshTokenHash hash,
        IClock clock,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(clock);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException(RefreshTokenErrors.LifetimeInvalid);
        }

        var now = clock.UtcNow;
        var token = new RefreshToken(id, userId, hash, now, now + lifetime);
        token.RaiseEvent(new RefreshTokenIssued(Guid.NewGuid(), now, id, userId, token.ExpiresAt));
        return token;
    }

    /// <summary>
    /// Rotates this token: this instance is marked revoked and pointed at the new
    /// token; a fresh <see cref="RefreshToken"/> is returned with its own state and
    /// its own pending <see cref="RefreshTokenIssued"/> event.
    /// </summary>
    /// <remarks>
    /// Method shape: instance method on the receiver, returning the new aggregate.
    /// The Identity domain brief lists two candidates ((a) instance method, (b)
    /// static <c>Rotate(old, …)</c>) and asks the modeler to pick when
    /// <c>domain-modeling.md</c> is silent — which it is for the
    /// "returns new aggregate AND mutates receiver" shape. Shape (a) was chosen because:
    /// <list type="bullet">
    ///   <item>It keeps the mutation entry point a verb on the aggregate, consistent with the rest of the rule set ("State changes only via domain methods (verbs).").</item>
    ///   <item>The active-state precondition naturally lives on the receiver.</item>
    ///   <item>The new aggregate carries its own <see cref="RefreshTokenIssued"/> event, and the receiver carries the <see cref="RefreshTokenRotated"/> event — so each aggregate emits the events that describe its own state change, which is what the outbox interceptor expects.</item>
    /// </list>
    /// </remarks>
    /// <param name="newId">Identifier of the replacement token.</param>
    /// <param name="newHash">SHA-256 hash of the new opaque token.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="newLifetime">Strictly-positive lifetime for the new token.</param>
    /// <returns>The newly issued replacement token, already populated with a <see cref="RefreshTokenIssued"/> event.</returns>
    /// <exception cref="DomainException">
    /// Thrown with one of:
    /// <list type="bullet">
    ///   <item><see cref="RefreshTokenErrors.CannotRotateAlreadyRotatedToken"/> if this token has already been rotated.</item>
    ///   <item><see cref="RefreshTokenErrors.CannotRotateRevokedToken"/> if this token was revoked without rotation.</item>
    ///   <item><see cref="RefreshTokenErrors.CannotRotateExpiredToken"/> if this token has expired.</item>
    ///   <item><see cref="RefreshTokenErrors.LifetimeInvalid"/> if <paramref name="newLifetime"/> is non-positive.</item>
    /// </list>
    /// </exception>
    public RefreshToken Rotate(
        RefreshTokenId newId,
        RefreshTokenHash newHash,
        IClock clock,
        TimeSpan newLifetime)
    {
        ArgumentNullException.ThrowIfNull(newHash);
        ArgumentNullException.ThrowIfNull(clock);

        // Check the three "not active" cases in the order that produces the most
        // informative error: rotation-already-done is the strongest signal
        // (suspected replay / theft per ADR 0005), then explicit revocation, then expiry.
        if (IsRotated)
        {
            throw new DomainException(RefreshTokenErrors.CannotRotateAlreadyRotatedToken);
        }

        if (RevokedAt is not null)
        {
            throw new DomainException(RefreshTokenErrors.CannotRotateRevokedToken);
        }

        var now = clock.UtcNow;
        if (now >= ExpiresAt)
        {
            throw new DomainException(RefreshTokenErrors.CannotRotateExpiredToken);
        }

        if (newLifetime <= TimeSpan.Zero)
        {
            throw new DomainException(RefreshTokenErrors.LifetimeInvalid);
        }

        // Issue the replacement first so any failure in its construction does not
        // leave this aggregate in a half-mutated state. Issue() raises its own
        // RefreshTokenIssued event.
        var replacement = new RefreshToken(newId, UserId, newHash, now, now + newLifetime);
        replacement.RaiseEvent(new RefreshTokenIssued(Guid.NewGuid(), now, newId, UserId, replacement.ExpiresAt));

        // Now mutate this aggregate: revoke and point at the replacement.
        RevokedAt = now;
        ReplacedByTokenId = newId;
        IncrementVersion();
        RaiseEvent(new RefreshTokenRotated(Guid.NewGuid(), now, Id, newId, UserId));

        return replacement;
    }

    /// <summary>
    /// Explicitly revokes this token (logout, admin action). Idempotent on tokens
    /// that are already explicitly revoked: no event, no version bump. Rejects
    /// rotated tokens loudly — rotation already implies revocation and an explicit
    /// revoke on top of it is a contract violation in the caller.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="RefreshTokenErrors.CannotRevokeRotatedToken"/> when
    /// this token has been rotated.
    /// </exception>
    public void Revoke(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        // Identity domain brief: rotated tokens cannot be explicitly revoked.
        if (IsRotated)
        {
            throw new DomainException(RefreshTokenErrors.CannotRevokeRotatedToken);
        }

        // Idempotent on already-revoked-but-not-rotated tokens.
        if (RevokedAt is not null)
        {
            return;
        }

        var now = clock.UtcNow;
        RevokedAt = now;
        IncrementVersion();
        RaiseEvent(new RefreshTokenRevoked(Guid.NewGuid(), now, Id, UserId));
    }
}
