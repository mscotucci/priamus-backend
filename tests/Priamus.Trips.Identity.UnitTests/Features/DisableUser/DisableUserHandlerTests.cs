using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.Events;
using Priamus.Trips.Identity.Features.DisableUser;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Identity.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Features.DisableUser;

/// <summary>
/// Unit tests for <see cref="DisableUserHandler"/> covering the paths that do not
/// require a running database.
///
/// Paths tested here (no I/O required):
/// - Self-disable rejection: returns before any DB read.
/// - Aggregate-level idempotency: a second Disable on an already-disabled
///   User does not raise a new UserDisabled event (domain-level regression guard).
///
/// Paths that require a real database (tested in the integration project):
/// - Happy path (user found, active → Disabled, audit row written).
/// - Not found (no matching row → UserErrors.NotFound).
/// - Idempotent DB round-trip (already-disabled → 204, no new audit row).
/// </summary>
public sealed class DisableUserHandlerTests
{
    // ── Test 1: Self-disable short-circuits before any DB access ─────────────

    [Fact]
    public async Task HandleAsync_WhenIdMatchesCurrentUser_ReturnsSelfDisableErrorWithoutDbAccess()
    {
        // Arrange
        var actorId = new UserId(new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(actorId);

        var clock = Substitute.For<IClock>();

        // IdentityDbContext constructed with a deliberately invalid connection string.
        // EF Core never opens a connection unless a query is executed, so constructing
        // the context here is safe and proves that the handler exits before any DB call.
        var dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unit-test-no-connect;Database=nodb;Username=nobody;Password=nobody")
            .Options;
        await using var db = new IdentityDbContext(dbOptions);

        var handler = new DisableUserHandler(
            db,
            currentUser,
            clock,
            NullLogger<DisableUserHandler>.Instance);

        // Act — pass the same Guid that the current user carries
        var result = await handler.HandleAsync(actorId.Value, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue(
            "Self-disable must be rejected before the user is loaded from the database.");
        result.Error.ShouldBe(UserErrors.CannotDisableSelf,
            "The returned error must be UserErrors.CannotDisableSelf, not a generic error.");
        result.Error.Code.ShouldBe(UserErrors.CannotDisableSelf.Code);

        // The clock must never be consulted — the handler returns before any domain call.
        _ = clock.DidNotReceive().UtcNow;
    }

    // ── Test 2: Domain-level idempotency of User.Disable ─────────────────────
    //
    // This test does NOT test the handler; it verifies the regression-sensitive
    // guarantee that the User aggregate itself emits no UserDisabled event on a
    // second call to Disable. This is the underlying invariant relied upon by the
    // handler's "idempotent no-op" behaviour.

    [Fact]
    public void Disable_WhenCalledTwiceOnSameUser_DoesNotRaiseSecondEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();

        user.Disable(clock, TestDataBuilders.AdminId);
        var versionAfterFirstDisable = user.Version;
        user.ClearDomainEvents();

        // Act — second Disable on an already-disabled user
        user.Disable(clock, TestDataBuilders.AdminId);

        // Assert — idempotent: no new event, no version bump
        user.DomainEvents.Count.ShouldBe(0,
            "A second Disable on an already-disabled user must not raise a new UserDisabled event.");
        user.Version.ShouldBe(versionAfterFirstDisable,
            "Version must not increment on an idempotent no-op Disable.");
    }

    // ── Test 3: Domain-level happy path: Disable raises UserDisabled event ────

    [Fact]
    public void Disable_WhenCalledOnActiveUser_RaisesUserDisabledEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();
        user.ClearDomainEvents(); // clear the UserCreated event from Build()

        // Act
        user.Disable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Disabled);
        user.DomainEvents.Count.ShouldBe(1,
            "Disable on an active user must raise exactly one UserDisabled domain event.");
        var evt = user.DomainEvents[0].ShouldBeOfType<UserDisabled>();
        evt.UserId.ShouldBe(userId);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }
}
