using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Domain.RefreshTokens;

public sealed class RefreshTokenTests
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    // ── Issue ────────────────────────────────────────────────────────────────

    [Fact]
    public void Issue_WithValidArguments_ReturnsToken()
    {
        // Arrange
        var id = RefreshTokenId.New();
        var userId = UserId.New();
        var hash = TestDataBuilders.ValidRefreshTokenHash();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var token = RefreshToken.Issue(id, userId, hash, clock, DefaultLifetime);

        // Assert
        token.Id.ShouldBe(id);
        token.UserId.ShouldBe(userId);
        token.TokenHash.ShouldBe(hash);
        token.CreatedAt.ShouldBe(clock.UtcNow);
        token.ExpiresAt.ShouldBe(clock.UtcNow + DefaultLifetime);
        token.RevokedAt.ShouldBeNull();
        token.ReplacedByTokenId.ShouldBeNull();
        token.IsRotated.ShouldBeFalse();
    }

    [Fact]
    public void Issue_WithValidArguments_RaisesNoDomainEvent()
    {
        // Issuing a token no longer writes an audit row, so it raises no event.
        // Arrange
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var token = RefreshToken.Issue(
            RefreshTokenId.New(), UserId.New(), TestDataBuilders.ValidRefreshTokenHash(), clock, DefaultLifetime);

        // Assert
        token.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Issue_WithZeroLifetime_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => RefreshToken.Issue(
            RefreshTokenId.New(), UserId.New(), TestDataBuilders.ValidRefreshTokenHash(),
            TestDataBuilders.DefaultClock(), TimeSpan.Zero);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.LifetimeInvalid.Code);
    }

    [Fact]
    public void Issue_WithNegativeLifetime_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => RefreshToken.Issue(
            RefreshTokenId.New(), UserId.New(), TestDataBuilders.ValidRefreshTokenHash(),
            TestDataBuilders.DefaultClock(), TimeSpan.FromSeconds(-1));

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.LifetimeInvalid.Code);
    }

    [Fact]
    public void Issue_WithNullHash_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => RefreshToken.Issue(
            RefreshTokenId.New(), UserId.New(), null!,
            TestDataBuilders.DefaultClock(), DefaultLifetime);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Issue_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => RefreshToken.Issue(
            RefreshTokenId.New(), UserId.New(), TestDataBuilders.ValidRefreshTokenHash(),
            null!, DefaultLifetime);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── IsActive ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_OnFreshToken_ReturnsTrue()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();

        // Assert
        token.IsActive(clock).ShouldBeTrue();
    }

    [Fact]
    public void IsActive_AfterExpiry_ReturnsFalse()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).WithLifetime(TimeSpan.FromMinutes(1)).Build();

        clock.Advance(TimeSpan.FromMinutes(2)); // past expiry

        // Assert
        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void IsActive_OnRevokedToken_ReturnsFalse()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Revoke(clock);

        // Assert
        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void IsActive_OnRotatedToken_ReturnsFalse()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Assert
        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void IsActive_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var token = TestDataBuilders.ARefreshToken().Build();

        // Act
        Action act = () => { var _ = token.IsActive(null!); };

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── Rotate ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rotate_OnActiveToken_RevokesOldAndReturnsNewToken()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var oldToken = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        var newId = RefreshTokenId.New();
        var newHash = TestDataBuilders.ValidRefreshTokenHash('c');

        // Act
        var newToken = oldToken.Rotate(newId, newHash, clock, DefaultLifetime);

        // Assert — old token mutated
        oldToken.IsRotated.ShouldBeTrue();
        oldToken.RevokedAt.ShouldNotBeNull();
        oldToken.ReplacedByTokenId.ShouldBe(newId);

        // Assert — new token issued
        newToken.Id.ShouldBe(newId);
        newToken.TokenHash.ShouldBe(newHash);
        newToken.UserId.ShouldBe(oldToken.UserId);
        newToken.RevokedAt.ShouldBeNull();
        newToken.IsRotated.ShouldBeFalse();
    }

    [Fact]
    public void Rotate_OnActiveToken_IncrementsOldTokenVersion()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var oldToken = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        var initialVersion = oldToken.Version;

        // Act
        oldToken.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Assert
        oldToken.Version.ShouldBe(initialVersion + 1);
    }

    [Fact]
    public void Rotate_OnActiveToken_RaisesNoDomainEventOnEitherToken()
    {
        // A renewal no longer writes audit rows, so neither token raises an event.
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var oldToken = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        oldToken.ClearDomainEvents();

        // Act
        var newToken = oldToken.Rotate(
            RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Assert
        oldToken.DomainEvents.ShouldBeEmpty();
        newToken.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Rotate_WithZeroNewLifetime_ThrowsDomainException()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();

        // Act
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, TimeSpan.Zero);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.LifetimeInvalid.Code);
    }

    [Fact]
    public void Rotate_OnAlreadyRotatedToken_ThrowsCannotRotateAlreadyRotatedToken()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Act — attempt second rotation
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('d'), clock, DefaultLifetime);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.CannotRotateAlreadyRotatedToken.Code);
    }

    [Fact]
    public void Rotate_OnRevokedButNotRotatedToken_ThrowsCannotRotateRevokedToken()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Revoke(clock);

        // Act
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.CannotRotateRevokedToken.Code);
    }

    [Fact]
    public void Rotate_OnExpiredToken_ThrowsCannotRotateExpiredToken()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken()
            .WithClock(clock)
            .WithLifetime(TimeSpan.FromMinutes(1))
            .Build();

        clock.Advance(TimeSpan.FromMinutes(2)); // past expiry

        // Act
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.CannotRotateExpiredToken.Code);
    }

    /// <summary>
    /// Error priority: when a token is simultaneously rotated AND revoked AND expired,
    /// the error must be CannotRotateAlreadyRotatedToken (strongest signal per the ADR).
    /// </summary>
    [Fact]
    public void Rotate_WhenSimultaneouslyRotatedAndRevokedAndExpired_ThrowsCannotRotateAlreadyRotatedToken()
    {
        // Arrange — issue a short-lived token, rotate it (which also revokes it), then advance past expiry
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken()
            .WithClock(clock)
            .WithLifetime(TimeSpan.FromMinutes(1))
            .Build();

        // Rotate (marks as rotated + revoked)
        token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Advance past original expiry so the token is also expired
        clock.Advance(TimeSpan.FromMinutes(2));

        // The token is now: IsRotated=true, RevokedAt != null, and the original ExpiresAt is in the past
        token.IsRotated.ShouldBeTrue();
        token.RevokedAt.ShouldNotBeNull();

        // Act — try to rotate again
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('d'), clock, DefaultLifetime);

        // Assert — IsRotated check wins (first guard in the switch)
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.CannotRotateAlreadyRotatedToken.Code);
    }

    [Fact]
    public void Rotate_WithNullHash_ThrowsArgumentNullException()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();

        // Act
        var act = () => token.Rotate(RefreshTokenId.New(), null!, clock, DefaultLifetime);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Rotate_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var token = TestDataBuilders.ARefreshToken().Build();

        // Act
        var act = () => token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), null!, DefaultLifetime);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── Revoke ───────────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_OnActiveToken_SetsRevokedAt()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();

        // Act
        token.Revoke(clock);

        // Assert
        token.RevokedAt.ShouldNotBeNull();
        token.RevokedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Revoke_OnActiveToken_IncrementsVersion()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        var initialVersion = token.Version;

        // Act
        token.Revoke(clock);

        // Assert
        token.Version.ShouldBe(initialVersion + 1);
    }

    [Fact]
    public void Revoke_OnActiveToken_RaisesNoDomainEvent()
    {
        // A revocation is audited by the handler's own row, not by a domain event.
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.ClearDomainEvents();

        // Act
        token.Revoke(clock);

        // Assert
        token.DomainEvents.ShouldBeEmpty();
        token.RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Revoke_OnAlreadyRevokedButNotRotatedToken_IsIdempotent_NoEventRaised()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Revoke(clock);
        var versionAfterFirstRevoke = token.Version;
        token.ClearDomainEvents();

        // Act — second Revoke on already-revoked token
        token.Revoke(clock);

        // Assert — idempotent: no new event, no version bump
        token.DomainEvents.Count.ShouldBe(0);
        token.Version.ShouldBe(versionAfterFirstRevoke);
    }

    [Fact]
    public void Revoke_OnRotatedToken_ThrowsCannotRevokeRotatedToken()
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();
        var token = TestDataBuilders.ARefreshToken().WithClock(clock).Build();
        token.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('c'), clock, DefaultLifetime);

        // Act
        var act = () => token.Revoke(clock);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.CannotRevokeRotatedToken.Code);
    }

    [Fact]
    public void Revoke_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var token = TestDataBuilders.ARefreshToken().Build();

        // Act
        var act = () => token.Revoke(null!);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }
}
